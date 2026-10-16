using System.Collections.Generic;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>
    /// Действия с Revit, нужные командам канала. Все вызовы — в потоке Revit (обработчик ExternalEvent).
    /// Проверку аргументов, поиск документов, коды ошибок и формат ответа делает <see cref="RequestDispatcher"/>;
    /// исполнитель только вызывает Revit API. Исключения исполнителя становятся <c>REVIT_ERROR</c>,
    /// если это не <see cref="HostCommandException"/>.
    /// </summary>
    public interface IRequestExecutor
    {
        /// <summary>Номер версии Revit, например "2022".</summary>
        string RevitVersion { get; }

        /// <summary>Открытые документы без связанных файлов; у активного в UI <see cref="DocumentInfo.IsActive"/> = true.</summary>
        IReadOnlyList<DocumentInfo> Documents();

        /// <summary>Создаёт новый метрический проект, сохраняет его в <paramref name="savePath"/> и закрывает фоновую копию.</summary>
        void CreateProjectFile(string savePath);

        /// <summary>Открывает файл в UI и делает его активным; возвращает открытый документ.</summary>
        DocumentInfo OpenAndActivate(string path);

        /// <summary>Закрывает неактивный документ без сохранения.</summary>
        void Close(DocumentInfo document);

        /// <summary>Прогон тестов; <paramref name="activeDocument"/> получают тесты с Model = "@active" (null — у них статус error).</summary>
        TestRunReport RunTests(string? filter, IReadOnlyDictionary<string, string> parameters, DocumentInfo? activeDocument);
    }

    /// <summary>Обработка одного запроса очереди (в потоке Revit).</summary>
    public interface IRequestHandler
    {
        /// <param name="request">Запрос.</param>
        /// <param name="idle">Значение <c>ping.idle</c>: Idling срабатывал после предыдущего запроса и очередь пуста.</param>
        HostResponse Handle(HostRequest request, bool idle);
    }
}
