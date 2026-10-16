using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>Журнал шагов: префиксы строк, хвост для сообщений об ошибках, журнал текущего теста.</summary>
    [TestFixture]
    [NonParallelizable]
    public class UiLogTests
    {
        private Action<string> _sink;
        private List<string> _written;

        [SetUp]
        public void CaptureSink()
        {
            _sink = UiLog.Sink;
            _written = new List<string>();
            UiLog.Sink = _written.Add;
        }

        [TearDown]
        public void RestoreSink()
        {
            UiLog.Sink = _sink;
        }

        [Test]
        public void Lines_have_kind_prefixes_and_go_to_sink()
        {
            UiLog.BeginTest("Ns.Class.Test");
            UiLog.Step("открыть панель");
            UiLog.Action("нажата кнопка");
            UiLog.Warn("окно закрыто по каталогу");
            Assert.That(_written, Has.Count.EqualTo(4));
            Assert.That(_written[0], Does.EndWith(" === Ns.Class.Test"));
            Assert.That(_written[1], Does.EndWith(" > открыть панель"));
            Assert.That(_written[2], Does.EndWith("   нажата кнопка"));
            Assert.That(_written[3], Does.EndWith(" ! окно закрыто по каталогу"));
        }

        [Test]
        public void BeginTest_starts_new_dump_but_keeps_recent_tail()
        {
            UiLog.BeginTest("первый");
            UiLog.Step("шаг первого теста");
            UiLog.BeginTest("второй");
            UiLog.Step("шаг второго теста");
            Assert.That(UiLog.Dump(), Does.Not.Contain("шаг первого теста").And.Contain("шаг второго теста"));
            IReadOnlyList<string> recent = UiLog.Recent(4);
            Assert.That(recent, Has.Count.EqualTo(4));
            Assert.That(recent[1], Does.EndWith("шаг первого теста"), "хвост журнала общий для прогона");
        }
    }
}
