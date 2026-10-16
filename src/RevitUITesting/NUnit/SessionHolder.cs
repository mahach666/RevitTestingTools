using System;

namespace RevitUITesting.NUnit
{
    /// <summary>Один объект сеанса на процесс тестов: запуск при первом запросе, повторный запуск, если прежний умер; End — освобождение.</summary>
    internal sealed class SessionHolder<T> where T : class
    {
        private readonly object _sync = new object();
        private readonly Func<T> _start;
        private readonly Func<T, bool> _isAlive;
        private readonly Action<T> _end;
        private T _current;

        internal SessionHolder(Func<T> start, Func<T, bool> isAlive, Action<T> end)
        {
            _start = start ?? throw new ArgumentNullException(nameof(start));
            _isAlive = isAlive ?? throw new ArgumentNullException(nameof(isAlive));
            _end = end ?? throw new ArgumentNullException(nameof(end));
        }

        internal T Current
        {
            get
            {
                lock (_sync)
                {
                    return _current;
                }
            }
        }

        internal T Acquire(out bool started)
        {
            lock (_sync)
            {
                if (_current != null && _isAlive(_current))
                {
                    started = false;
                    return _current;
                }
                if (_current != null)
                {
                    T dead = _current;
                    _current = null;
                    UiLog.Warn("сеанс Revit мёртв (процесс завершился) — освобождаю его и запускаю Revit заново");
                    _end(dead);
                }
                _current = _start();
                started = true;
                return _current;
            }
        }

        /// <summary>
        /// Заменить текущий объект новым под замком: stop (например, Quit) и освобождение прежнего, затем запуск нового — он становится текущим
        /// и освобождается в <see cref="End"/>. Сбой stop — прежний всё равно освобождается, новый не запускается, текущего нет; сбой запуска —
        /// текущего нет (следующий <see cref="Acquire"/> запустит заново). Текущего нет — InvalidOperationException.
        /// </summary>
        internal T Replace(Action<T> stop)
        {
            if (stop == null)
            {
                throw new ArgumentNullException(nameof(stop));
            }
            lock (_sync)
            {
                T old = _current ?? throw new InvalidOperationException("Сеанса нет: заменять нечего.");
                _current = null;
                try
                {
                    stop(old);
                }
                catch (Exception stopError)
                {
                    // прежний объект освобождается и при сбое остановки; оба сбоя видны, если упало и освобождение
                    try
                    {
                        _end(old);
                    }
                    catch (Exception endError)
                    {
                        throw new AggregateException("Остановка и освобождение прежнего объекта сеанса завершились ошибками", stopError, endError);
                    }
                    throw;
                }
                _end(old);
                _current = _start();
                return _current;
            }
        }

        internal void End()
        {
            lock (_sync)
            {
                T current = _current;
                _current = null;
                if (current != null)
                {
                    _end(current);
                }
            }
        }
    }
}
