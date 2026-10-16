using System;

namespace RevitUITesting
{
    /// <summary>Элемент каталога показывается не всегда (условие — в тексте): при сверке с экраном его отсутствие не ошибка.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class ShownWhenAttribute : Attribute
    {
        public ShownWhenAttribute(string condition)
        {
            Condition = condition;
        }

        public string Condition { get; }
    }
}
