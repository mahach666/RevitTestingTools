using System;

namespace RevitUITesting.Uia
{
    /// <summary>Строгое чтение для решений: «занят» — <see cref="AppBusyException"/>, «исчез» — явный результат вызывающего.</summary>
    internal static class UiRead
    {
        /// <summary>
        /// Прочитать значение UIA строго (<see cref="UiQuery.Read{T}"/>: пока приложение занято, повторять до timeout, затем <see cref="AppBusyException"/>).
        /// Элемент исчез из дерева — результат whenGone: вызывающий сам решает, что для него значит «элемента уже нет».
        /// </summary>
        internal static T OrGone<T>(Func<T> read, T whenGone, TimeSpan timeout, string what)
        {
            try
            {
                return UiQuery.Read(read, timeout, what);
            }
            catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Gone)
            {
                // исчезновение элемента здесь — не сбой, а ответ «его больше нет»; занятость и прочие ошибки сюда не попадают
                return whenGone;
            }
        }
    }
}
