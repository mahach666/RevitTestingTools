using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FlaUI.Core.Exceptions;
using NUnit.Framework;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>Ожидания и классификация сбоев UI Automation (без приложения: сбои моделируются исключениями с кодами HRESULT).</summary>
    [TestFixture]
    [NonParallelizable]
    public class WaitTests
    {
        private TimeSpan _poll;
        private TimeSpan _default;

        [SetUp]
        public void FastPolling()
        {
            _poll = Wait.PollInterval;
            _default = Wait.DefaultTimeout;
            Wait.PollInterval = TimeSpan.FromMilliseconds(10);
            Wait.DefaultTimeout = TimeSpan.FromMilliseconds(200);
        }

        [TearDown]
        public void RestorePolling()
        {
            Wait.PollInterval = _poll;
            Wait.DefaultTimeout = _default;
        }

        private static COMException Com(uint code)
        {
            return new COMException("uia", unchecked((int)code));
        }

        [Test]
        public void Uia_timeout_codes_are_busy()
        {
            Assert.That(UiFault.Classify(Com(0x80131505)), Is.EqualTo(UiFaultKind.Busy));
            Assert.That(UiFault.Classify(Com(0x8001011F)), Is.EqualTo(UiFaultKind.Busy));
            // HRESULT_FROM_WIN32(ERROR_TIMEOUT): FindAllChildren рабочего стола в первые секунды старта Revit (closing-run-2022.txt)
            Assert.That(UiFault.Classify(Com(0x800705B4)), Is.EqualTo(UiFaultKind.Busy));
            // так FlaUI (Tools.Com.ConvertException) оборачивает UIA_E_TIMEOUT
            Assert.That(UiFault.Classify(new TimeoutException("UIA Timeout", Com(0x80131505))), Is.EqualTo(UiFaultKind.Busy));
        }

        [Test]
        public void Element_not_available_is_gone()
        {
            Assert.That(UiFault.Classify(Com(0x80040201)), Is.EqualTo(UiFaultKind.Gone));
            Assert.That(UiFault.Classify(new ElementNotAvailableException()), Is.EqualTo(UiFaultKind.Gone));
        }

        [Test]
        public void Other_failures_are_not_transient()
        {
            Assert.That(UiFault.Classify(Com(0x80004005)), Is.EqualTo(UiFaultKind.Other), "E_FAIL не считается занятостью");
            Assert.That(UiFault.Classify(new NullReferenceException()), Is.EqualTo(UiFaultKind.Other));
            Assert.That(UiFault.Classify(new TimeoutException("без COM")), Is.EqualTo(UiFaultKind.Other));
            Assert.That(UiFault.Classify(new UiWaitTimeoutException("x", TimeSpan.Zero, TimeSpan.Zero, null, Com(0x80131505))), Is.EqualTo(UiFaultKind.Other),
                "собственный таймаут ожидания — не занятость, даже если внутри таймаут UIA");
        }

        [Test]
        public void Nested_app_busy_is_busy_for_outer_wait()
        {
            Assert.That(UiFault.Classify(new AppBusyException("x", TimeSpan.Zero, TimeSpan.Zero, 3, null)), Is.EqualTo(UiFaultKind.Busy));
        }

        [Test]
        public void Timeout_exception_is_a_TimeoutException_with_description_and_observation()
        {
            UiWaitTimeoutException ex = new UiWaitTimeoutException("панель открыта", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), "панели нет", null);
            Assert.That(ex, Is.InstanceOf<TimeoutException>());
            Assert.That(ex.Message, Does.Contain("Не дождались: панель открыта (таймаут 5 с)").And.Contain("последнее наблюдение: панели нет"));
        }

        [Test]
        public void Until_with_timeout_checks_at_least_once_with_zero_timeout()
        {
            int calls = 0;
            Wait.Until(() => ++calls > 0, TimeSpan.Zero);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Until_with_timeout_checks_again_after_deadline()
        {
            int calls = 0;
            Assert.Throws<UiWaitTimeoutException>(() => Wait.Until(() => ++calls < 0, TimeSpan.FromMilliseconds(30), "никогда"));
            Assert.That(calls, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Until_with_timeout_propagates_programming_errors_at_once()
        {
            int calls = 0;
            Assert.Throws<InvalidOperationException>(() => Wait.Until(() =>
            {
                calls++;
                throw new InvalidOperationException("ошибка в условии");
            }, TimeSpan.FromSeconds(5)));
            Assert.That(calls, Is.EqualTo(1), "ошибка в условии — не «ещё не готово»");
        }

        [Test]
        public void Until_uses_expression_as_description()
        {
            bool ready = false;
            UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => Wait.Until(() => ready));
            Assert.That(ex.Description, Does.Contain("ready"));
            ex = Assert.Throws<UiWaitTimeoutException>(() => Wait.Until(() => ready, TimeSpan.FromMilliseconds(20)));
            Assert.That(ex.Description, Does.Contain("ready"));
        }

        [Test]
        public void Until_propagates_programming_errors_at_once()
        {
            string text = null;
            Assert.Throws<NullReferenceException>(() => Wait.Until(() => text.Length > 0));
        }

        [Test]
        public void Until_treats_busy_app_as_not_ready()
        {
            int calls = 0;
            Wait.Until(() =>
            {
                if (++calls > 2)
                {
                    return true;
                }
                throw Com(0x80131505);
            });
            Assert.That(calls, Is.GreaterThan(2));
        }

        [Test]
        public void Only_busy_faults_give_app_busy_exception()
        {
            AppBusyException ex = Assert.Throws<AppBusyException>(() => Wait.Until(() => throw Com(0x80131505)));
            Assert.That(ex.BusyFaults, Is.GreaterThan(0));
            Assert.That(ex, Is.InstanceOf<TimeoutException>());
            Assert.That(ex.Message, Does.Contain("Приложение было занято"));
        }

        [Test]
        public void For_returns_value()
        {
            int calls = 0;
            string value = Wait.For(() => ++calls < 3 ? null : "готово");
            Assert.That(value, Is.EqualTo("готово"));
            calls = 0;
            Assert.That(Wait.For(() => ++calls < 3 ? null : "готово", TimeSpan.FromSeconds(5)), Is.EqualTo("готово"));
        }

        [Test]
        public void Holds_returns_false_without_timeout_exception_and_diagnostics()
        {
            Func<string> saved = WaitDiagnostics.Provider;
            int calls = 0;
            try
            {
                WaitDiagnostics.Provider = () => (++calls).ToString();
                Assert.That(Wait.Holds(() => false, TimeSpan.FromMilliseconds(20)), Is.False);
                Assert.That(Wait.Holds(() => true, TimeSpan.FromMilliseconds(20)), Is.True);
                Assert.That(calls, Is.Zero, "Holds не строит исключение таймаута");
            }
            finally
            {
                WaitDiagnostics.Provider = saved;
            }
        }

        [Test]
        public void Holds_does_not_swallow_condition_errors()
        {
            Assert.Throws<InvalidOperationException>(() => Wait.Holds(() => throw new InvalidOperationException("сбой"), TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void RecordUntil_keeps_distinct_consecutive_values_until_done()
        {
            string[] feed = { "a", "a", "b", "b", "b", "c" };
            int i = 0;
            IReadOnlyList<string> values = Wait.RecordUntil(() => feed[Math.Min(i++, feed.Length - 1)], v => v == "c", TimeSpan.FromSeconds(5));
            Assert.That(values, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void RecordUntil_skips_busy_faults_and_reports_values_on_timeout()
        {
            int calls = 0;
            UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => Wait.RecordUntil(() =>
            {
                if (++calls % 2 == 0)
                {
                    throw Com(0x80131505);
                }
                return "загрузка";
            }, v => v == "готово", TimeSpan.FromMilliseconds(50)));
            Assert.That(ex.Message, Does.Contain("загрузка"));
        }

        [Test]
        public void RecordUntil_when_every_sample_is_busy_gives_app_busy_exception()
        {
            AppBusyException ex = Assert.Throws<AppBusyException>(() => Wait.RecordUntil<string>(() => throw Com(0x80131505), v => true, TimeSpan.FromMilliseconds(50)));
            Assert.That(ex.BusyFaults, Is.GreaterThan(0));
        }

        [Test]
        public void Diagnostics_provider_text_and_recent_steps_are_in_message()
        {
            Func<string> saved = WaitDiagnostics.Provider;
            try
            {
                UiLog.Step("шаг перед отказом");
                WaitDiagnostics.Provider = () => "Окна приложения: «Save changes?»";
                UiWaitTimeoutException ex = new UiWaitTimeoutException("условие", TimeSpan.Zero, TimeSpan.Zero, null, null);
                Assert.That(ex.Diagnostics, Does.Contain("Save changes?").And.Contain("шаг перед отказом"));
                Assert.That(ex.Message, Does.Contain("Save changes?"));
            }
            finally
            {
                WaitDiagnostics.Provider = saved;
            }
        }

        [Test]
        public void Diagnostics_are_not_collected_again_while_collecting()
        {
            Func<string> saved = WaitDiagnostics.Provider;
            int calls = 0;
            try
            {
                // сбор диагностики сам упирается в занятое приложение: его исключение не должно запускать сбор заново
                WaitDiagnostics.Provider = () =>
                {
                    calls++;
                    throw new AppBusyException("перечень окон", TimeSpan.Zero, TimeSpan.Zero, 1, Com(0x80131505));
                };
                UiWaitTimeoutException ex = new UiWaitTimeoutException("условие", TimeSpan.Zero, TimeSpan.Zero, null, null);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(ex.Diagnostics, Does.Contain("Диагностика приложения не собрана").And.Contain("AppBusyException"));
            }
            finally
            {
                WaitDiagnostics.Provider = saved;
            }
        }

        [Test]
        public void Stays_true_when_condition_holds()
        {
            Assert.That(Wait.Stays(() => true, TimeSpan.FromMilliseconds(40)), Is.True);
        }

        [Test]
        public void Stays_false_when_condition_breaks()
        {
            int calls = 0;
            Assert.That(Wait.Stays(() => ++calls < 3, TimeSpan.FromMilliseconds(200)), Is.False);
        }

        [Test]
        public void Stays_without_successful_check_is_app_busy()
        {
            Assert.Throws<AppBusyException>(() => Wait.Stays(() => throw Com(0x8001011F), TimeSpan.FromMilliseconds(40)));
        }

        [Test]
        public void Stays_requires_completed_check_after_interval()
        {
            int calls = 0;
            Assert.Throws<AppBusyException>(() => Wait.Stays(() =>
            {
                if (++calls == 1)
                {
                    return true;
                }
                throw Com(0x80131505);
            }, TimeSpan.FromMilliseconds(30)), "конечное состояние не увидено — это не «осталось истинным»");
        }

        [Test]
        public void Stays_keeps_polling_after_interval_until_check_completes()
        {
            int calls = 0;
            bool stays = Wait.Stays(() =>
            {
                calls++;
                if (calls >= 2 && calls <= 6)
                {
                    throw Com(0x80131505);
                }
                return true;
            }, TimeSpan.FromMilliseconds(20));
            Assert.That(stays, Is.True);
            Assert.That(calls, Is.GreaterThanOrEqualTo(7));
        }

        [Test]
        public void Vanished_element_is_timeout_not_app_busy()
        {
            COMException gone = Com(0x80040201);
            UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => Wait.Until(() => throw gone));
            Assert.That(ex, Is.Not.InstanceOf<AppBusyException>(), "элемент исчез — приложение при этом не было занято");
            Assert.That(ex.InnerException, Is.SameAs(gone));
            Assert.Throws<UiWaitTimeoutException>(() => Wait.Stays(() => throw gone, TimeSpan.FromMilliseconds(20)));
        }
    }
}
