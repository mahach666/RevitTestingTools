using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RevitTestHost.Core.Execution;
using RevitTestHost.Core.Json;
using RevitTestHost.Core.Reporting;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>
    /// Выполняет команды протокола (контракт 1) через <see cref="IRequestExecutor"/>: проверяет аргументы,
    /// ищет документы, выставляет коды ошибок и строит <c>result</c> с точными именами полей.
    /// Никогда не бросает: любая ошибка превращается в ответ <c>ok:false</c>.
    /// </summary>
    public sealed class RequestDispatcher : IRequestHandler
    {
        private readonly IRequestExecutor _executor;
        private readonly ServeWorkspace _workspace;

        public RequestDispatcher(IRequestExecutor executor, ServeWorkspace workspace)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        }

        public HostResponse Handle(HostRequest request, bool idle)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            try
            {
                return HostResponse.Success(request.Id, Execute(request, idle));
            }
            catch (HostCommandException ex)
            {
                return HostResponse.Failure(request.Id, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                return HostResponse.Failure(request.Id, ErrorCodes.RevitError, ExceptionText.Describe(ex));
            }
        }

        private IJsonWritable Execute(HostRequest request, bool idle)
        {
            switch (request.Command)
            {
                case Commands.Ping: return Ping(idle);
                case Commands.Documents: return Documents();
                case Commands.Open: return Open(OpenArgs.Parse(request.Args));
                case Commands.Close: return Close(CloseArgs.Parse(request.Args));
                case Commands.Run: return Run(RunArgs.Parse(request.Args));
                default:
                    throw new HostCommandException(ErrorCodes.UnknownCommand,
                        $"Неизвестная команда '{request.Command}'. Поддерживаются: ping, documents, open, close, run.");
            }
        }

        private IJsonWritable Ping(bool idle)
        {
            var revitVersion = _executor.RevitVersion;
            var active = _executor.Documents().FirstOrDefault(d => d.IsActive);
            var pid = Process.GetCurrentProcess().Id;
            return new JsonPayload(w =>
            {
                w.BeginObject()
                    .Property("serverVersion", ServerInfo.ProtocolVersion)
                    .Property("revitVersion", revitVersion)
                    .Property("pid", pid)
                    .Property("idle", idle)
                    .Name("activeDocument");
                if (active == null) w.Null();
                else active.WriteSummary(w);
                w.EndObject();
            });
        }

        private IJsonWritable Documents()
        {
            var documents = _executor.Documents();
            return new JsonPayload(w =>
            {
                w.BeginObject().Name("documents").BeginArray();
                foreach (var d in documents) d.WriteFull(w);
                w.EndArray().EndObject();
            });
        }

        private IJsonWritable Open(OpenArgs args)
        {
            string path;
            if (args.IsNewProject)
            {
                path = _workspace.NextNewProjectPath();
                _executor.CreateProjectFile(path);
            }
            else
            {
                if (!File.Exists(args.Path))
                    throw new HostCommandException(ErrorCodes.NotFound, $"Файл модели не найден: {args.Path}");
                path = args.CopyToTemp ? _workspace.CopyModel(args.Path) : args.Path;
            }

            var opened = _executor.OpenAndActivate(path);
            return new JsonPayload(w => w.BeginObject()
                .Property("title", opened.Title)
                .Property("path", opened.Path)
                .EndObject());
        }

        private IJsonWritable Close(CloseArgs args)
        {
            var target = DocumentLookup.Find(_executor.Documents(), args.Title, args.Path);
            if (target.IsActive)
                throw new HostCommandException(ErrorCodes.ActiveDocument,
                    $"Документ '{target.Title}' активен в UI: Revit API не закрывает активный документ. " +
                    "Сначала откройте другой документ или закройте этот через UI.");
            _executor.Close(target);
            return new JsonPayload(w => w.BeginObject().Property("closed", true).EndObject());
        }

        private IJsonWritable Run(RunArgs args)
        {
            var documents = _executor.Documents();
            var target = args.Model == ModelKeys.ActiveDocument
                ? documents.FirstOrDefault(d => d.IsActive)
                : DocumentLookup.FindByModel(documents, args.Model);
            var report = _executor.RunTests(args.Filter, args.Parameters, target);
            return JsonReportWriter.AsJson(report);
        }
    }
}
