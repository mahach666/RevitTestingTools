using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Addin.Serve
{
    /// <summary>
    /// Достраивает дерево UI Automation Revit для внешних клиентов (RevitUITesting). В Revit 2025 и новее элемент UIA панели ленты
    /// (DataItem «UIFramework.RvtRibbonPanel») отдаёт потомков, только если peer содержимого панели уже создан, а сам Revit его не создаёт:
    /// снаружи кнопки встроенных панелей (Project Units и др.) не видны ни обходом, ни по точке. Живая проба Revit 2025.1: после
    /// CreatePeerForElement у элементов панели внешний обход находит Button «ID_SETTINGS_UNITS» с Invoke.
    /// Пока работает режим сервера, таймер диспетчера WPF создаёт peer каждому видимому элементу всех окон WPF процесса, у которого его ещё нет,
    /// и сбрасывает запомненных потомков (<see cref="ResetChildren"/>); обход ~1600 элементов занимает 0–11 мс (Revit 2025.1).
    /// </summary>
    internal sealed class AutomationPeerWarmer : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

        private readonly DispatcherTimer _timer;
        private readonly IServerLog _log;
        private bool _reported;

        private AutomationPeerWarmer(IServerLog log)
        {
            _log = log;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
            _timer.Tick += OnTick;
        }

        /// <summary>Запуск в потоке UI Revit (таймер работает в диспетчере потока, который его создал).</summary>
        public static AutomationPeerWarmer Start(IServerLog log)
        {
            var warmer = new AutomationPeerWarmer(log);
            warmer._timer.Start();
            return warmer;
        }

        public void Dispose()
        {
            _timer.Stop();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                var watch = Stopwatch.StartNew();
                var (visited, created) = WarmAll();
                if (!_reported)
                {
                    _reported = true;
                    _log.Info($"Peer UI Automation: первый обход окон WPF — элементов {visited}, создано peer {created}, {watch.ElapsedMilliseconds} мс; дальше каждые {Interval.TotalMilliseconds} мс.");
                }
            }
            catch (Exception ex)
            {
                // исключение в обработчике таймера диспетчера роняет Revit: останавливаем достройку, причина — в server.log
                _timer.Stop();
                _log.Error("Достройка peer UI Automation остановлена: " + ExceptionText.Describe(ex), ex);
            }
        }

        private static (int visited, int created) WarmAll()
        {
            int visited = 0, created = 0;
            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (source.CheckAccess() && !source.IsDisposed && source.RootVisual != null)
                    Warm(source.RootVisual, ref visited, ref created);
            }
            return (visited, created);
        }

        /// <summary>
        /// Peer запоминает потомков и пересчитывает их только по событиям раскладки при подписанном клиенте UIA; клиент без подписок
        /// (FlaUI в тестах) получает список, запомненный до построения панели, — пустой. Сброс пересчитывает его; у peer элементов
        /// списков (ItemAutomationPeer, DataItem) своего UIElement нет, обход визуального дерева их не встретит — сбрасываются здесь.
        /// </summary>
        private static void ResetChildren(AutomationPeer peer)
        {
            peer.ResetChildrenCache();
            var children = peer.GetChildren();
            if (children == null) return;
            foreach (var child in children)
            {
                if (child is ItemAutomationPeer)
                    child.ResetChildrenCache();
            }
        }

        /// <summary>Обход визуального дерева; невидимое поддерево пропускается (его peer снаружи не нужен, а неактивные вкладки ленты не построены).</summary>
        private static void Warm(Visual root, ref int visited, ref int created)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                visited++;
                if (node is UIElement element)
                {
                    if (!element.IsVisible)
                        continue;
                    var peer = UIElementAutomationPeer.FromElement(element);
                    if (peer == null && (peer = UIElementAutomationPeer.CreatePeerForElement(element)) != null)
                        created++;
                    if (peer != null)
                        ResetChildren(peer);
                }
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                    stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
    }
}
