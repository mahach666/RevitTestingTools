# Выпуск версии

1. Поднимите `Version` в [Directory.Build.props](Directory.Build.props). Версия общая для всех проектов.
2. Перенесите записи из раздела «Не выпущено» в [CHANGELOG.md](CHANGELOG.md) под заголовок новой версии с датой и поправьте ссылки внизу файла.
3. Проверьте локально:

   ```powershell
   dotnet build RevitTestingTools.sln -c Release
   dotnet test RevitTestingTools.sln -c Release --no-build
   powershell -ExecutionPolicy Bypass -File tools/pack-release.ps1
   ```

   Файлы релиза появятся в `artifacts/`: восемь архивов надстройки (по одному на год Revit 2020-2027), установщик надстройки `RevitTestHost-Addin-<версия>.msi`, CLI, инспектор, два пакета `.nupkg` с символами `.snupkg` и `SHA256SUMS.txt`.
4. Закоммитьте изменения, поставьте тег и отправьте его:

   ```powershell
   git tag -a v0.1.0 -m "v0.1.0"
   git push origin main v0.1.0
   ```

5. Workflow [release.yml](.github/workflows/release.yml) по тегу `v*` соберёт и проверит решение, подготовит те же файлы и создаст на GitHub черновик релиза с пометкой pre-release. Описание берётся из раздела версии в `CHANGELOG.md`. Проверьте черновик и опубликуйте его.
6. Тот же workflow сразу отправляет `RevitTestHost.Sdk` и `RevitUITesting` в nuget.org через Trusted Publishing: ключ API не хранится, GitHub Actions получает временный ключ по политике nuget.org (владелец `mahach`, репозиторий `mahach666/RevitTestingTools`, workflow `release.yml`). Публикуются пакеты с версией из тега; если `Version` в `Directory.Build.props` не совпадает с тегом, шаг падает и ничего не отправляет. Опубликованную версию на nuget.org нельзя удалить, только скрыть (unlist).

Перед выпуском на новую версию Revit проверьте, что надстройка в ней загружается, и повторите контрактные тесты (см. [docs/revit-ui-testing.md](docs/revit-ui-testing.md#контрактные-тесты)). Архив надстройки собирается против Revit API версии, указанной в параметре `-RevitVersion` скрипта `pack-release.ps1` (по умолчанию 2022).
