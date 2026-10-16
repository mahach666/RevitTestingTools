using System;
using FlaUI.Core;
using FlaUI.Core.Patterns;
using FlaUI.Core.Patterns.Infrastructure;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>ValuePattern узла <see cref="FakeNode"/>: запись меняет CurrentValue узла, чтение идёт через его свойство Value (со сбоями OnValueRead).</summary>
    internal sealed class FakeValuePattern : IAutomationPattern<IValuePattern>, IValuePattern
    {
        private readonly FakeNode _node;

        internal FakeValuePattern(FakeNode node)
        {
            _node = node;
        }

        public IValuePattern Pattern => this;

        public IValuePattern PatternOrDefault => this;

        public bool IsSupported => true;

        public bool TryGetPattern(out IValuePattern pattern)
        {
            pattern = this;
            return true;
        }

        public IValuePatternPropertyIds PropertyIds => _node.Automation.PropertyLibrary.Value;

        public AutomationProperty<bool> IsReadOnly => new AutomationProperty<bool>(PropertyIds.IsReadOnly, _node);

        public AutomationProperty<string> Value => new AutomationProperty<string>(PropertyIds.Value, _node);

        public void SetValue(string value)
        {
            Exception fault = _node.OnSetValue?.Invoke(++_node.SetValueCalls);
            if (fault != null)
            {
                throw fault;
            }
            _node.CurrentValue = value;
        }
    }
}
