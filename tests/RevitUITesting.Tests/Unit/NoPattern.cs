using FlaUI.Core;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Patterns.Infrastructure;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>Паттерн, которого у <see cref="FakeNode"/> нет: как у реального элемента без паттерна (IsSupported = false, PatternOrDefault = null).</summary>
    internal sealed class NoPattern<T> : IAutomationPattern<T>
        where T : IPattern
    {
        public T Pattern => throw new PatternNotSupportedException();

        public T PatternOrDefault => default(T);

        public bool IsSupported => false;

        public bool TryGetPattern(out T pattern)
        {
            pattern = default(T);
            return false;
        }
    }
}
