using System;
using System.Collections.Generic;
using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Identifiers;
using FlaUI.Core.Patterns;
using FlaUI.UIA3.Converters;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>
    /// Узел дерева UI Automation без приложения: дети и свойства (AutomationId, Name, ClassName, ControlType) задаются в тесте,
    /// сбои запросов (занят, исчез) — функциями OnFindChildren/OnProperty. Паттернов у узла нет (IsSupported = false); прочие члены FlaUI не поддерживаются.
    /// </summary>
    internal sealed class FakeNode : FrameworkAutomationElementBase
    {
        internal FakeNode(AutomationBase automation, string id, UiaControlType type = UiaControlType.Pane, string name = "", string className = "")
            : base(automation)
        {
            FakeId = id;
            FakeType = type;
            FakeName = name;
            FakeClassName = className;
        }

        internal string FakeId { get; }

        internal UiaControlType FakeType { get; }

        internal string FakeName { get; }

        internal string FakeClassName { get; }

        internal List<FakeNode> Kids { get; } = new List<FakeNode>();

        /// <summary>Сбой запроса детей (null — без сбоя); аргумент — номер вызова начиная с 1.</summary>
        internal Func<int, Exception> OnFindChildren { get; set; }

        /// <summary>Сбой чтения свойства (null — без сбоя); аргумент — номер чтения начиная с 1.</summary>
        internal Func<int, Exception> OnProperty { get; set; }

        /// <summary>Узел поддерживает ValuePattern (по умолчанию нет): запись через SetValue, чтение — свойство Value.</summary>
        internal bool SupportsValue { get; set; }

        /// <summary>Текущее значение ValuePattern; SetValue меняет его.</summary>
        internal string CurrentValue { get; set; } = "";

        /// <summary>Сбой чтения значения ValuePattern (null — без сбоя); аргумент — номер чтения начиная с 1.</summary>
        internal Func<int, Exception> OnValueRead { get; set; }

        internal int ValueReads { get; private set; }

        /// <summary>Сбой записи значения ValuePattern (null — без сбоя); аргумент — номер вызова SetValue начиная с 1.</summary>
        internal Func<int, Exception> OnSetValue { get; set; }

        internal int SetValueCalls { get; set; }

        /// <summary>Дескриптор окна, который узел отдаёт как NativeWindowHandle (по умолчанию нет — свойство не поддерживается).</summary>
        internal IntPtr FakeHandle { get; set; }

        /// <summary>Значение свойства IsOffscreen (по умолчанию узел на экране).</summary>
        internal bool FakeOffscreen { get; set; }

        /// <summary>Сбой чтения IsOffscreen (null — без сбоя); аргумент — номер чтения начиная с 1.</summary>
        internal Func<int, Exception> OnOffscreen { get; set; }

        internal int OffscreenReads { get; private set; }

        internal int FindChildrenCalls { get; private set; }

        internal int PropertyReads { get; private set; }

        internal AutomationElement Element => new AutomationElement(this);

        internal FakeNode Add(params FakeNode[] children)
        {
            Kids.AddRange(children);
            return this;
        }

        protected override object InternalGetPropertyValue(int propertyId, bool cached, bool useDefaultIfNotSupported)
        {
            PropertyReads++;
            Exception fault = OnProperty?.Invoke(PropertyReads);
            if (fault != null)
            {
                throw fault;
            }
            if (propertyId == Automation.PropertyLibrary.Value.Value.Id)
            {
                ValueReads++;
                Exception valueFault = OnValueRead?.Invoke(ValueReads);
                if (valueFault != null)
                {
                    throw valueFault;
                }
                return CurrentValue;
            }
            var ids = Automation.PropertyLibrary.Element;
            if (propertyId == ids.AutomationId.Id)
            {
                return FakeId;
            }
            if (propertyId == ids.Name.Id)
            {
                return FakeName;
            }
            if (propertyId == ids.ClassName.Id)
            {
                return FakeClassName;
            }
            if (propertyId == ids.NativeWindowHandle.Id && FakeHandle != IntPtr.Zero)
            {
                return FakeHandle.ToInt32();
            }
            if (propertyId == ids.IsOffscreen.Id)
            {
                OffscreenReads++;
                Exception offscreenFault = OnOffscreen?.Invoke(OffscreenReads);
                if (offscreenFault != null)
                {
                    throw offscreenFault;
                }
                return FakeOffscreen;
            }
            if (propertyId == ids.ControlType.Id)
            {
                return ControlTypeConverter.ToControlTypeNative(FakeType);
            }
            return Automation.NotSupportedValue;
        }

        public override AutomationElement[] FindAll(TreeScope treeScope, ConditionBase condition)
        {
            if (treeScope != TreeScope.Children || !(condition is TrueCondition))
            {
                throw new NotSupportedException("FakeNode: поддерживается только FindAllChildren");
            }
            FindChildrenCalls++;
            Exception fault = OnFindChildren?.Invoke(FindChildrenCalls);
            if (fault != null)
            {
                throw fault;
            }
            return Kids.ConvertAll(k => k.Element).ToArray();
        }

        public override void SetFocus()
        {
            throw new NotSupportedException();
        }

        protected override object InternalGetPattern(int patternId, bool cached)
        {
            throw new NotSupportedException();
        }

        public override AutomationElement FindFirst(TreeScope treeScope, ConditionBase condition)
        {
            throw new NotSupportedException();
        }

        public override AutomationElement[] FindAllWithOptions(TreeScope treeScope, ConditionBase condition, TreeTraversalOptions traversalOptions, AutomationElement root)
        {
            throw new NotSupportedException();
        }

        public override AutomationElement FindFirstWithOptions(TreeScope treeScope, ConditionBase condition, TreeTraversalOptions traversalOptions, AutomationElement root)
        {
            throw new NotSupportedException();
        }

        public override AutomationElement FindAt(TreeScope treeScope, int index, ConditionBase condition)
        {
            throw new NotSupportedException();
        }

        public override bool TryGetClickablePoint(out Point point)
        {
            throw new NotSupportedException();
        }

        public override ActiveTextPositionChangedEventHandlerBase RegisterActiveTextPositionChangedEvent(TreeScope treeScope, Action<AutomationElement, ITextRange> action)
        {
            throw new NotSupportedException();
        }

        public override AutomationEventHandlerBase RegisterAutomationEvent(EventId @event, TreeScope treeScope, Action<AutomationElement, EventId> action)
        {
            throw new NotSupportedException();
        }

        public override PropertyChangedEventHandlerBase RegisterPropertyChangedEvent(TreeScope treeScope, Action<AutomationElement, PropertyId, object> action, PropertyId[] properties)
        {
            throw new NotSupportedException();
        }

        public override StructureChangedEventHandlerBase RegisterStructureChangedEvent(TreeScope treeScope, Action<AutomationElement, StructureChangeType, int[]> action)
        {
            throw new NotSupportedException();
        }

        public override NotificationEventHandlerBase RegisterNotificationEvent(TreeScope treeScope, Action<AutomationElement, NotificationKind, NotificationProcessing, string, string> action)
        {
            throw new NotSupportedException();
        }

        public override TextEditTextChangedEventHandlerBase RegisterTextEditTextChangedEventHandler(TreeScope treeScope, TextEditChangeType textEditChangeType, Action<AutomationElement, TextEditChangeType, string[]> action)
        {
            throw new NotSupportedException();
        }

        public override void UnregisterActiveTextPositionChangedEventHandler(ActiveTextPositionChangedEventHandlerBase eventHandler)
        {
            throw new NotSupportedException();
        }

        public override void UnregisterAutomationEventHandler(AutomationEventHandlerBase eventHandler)
        {
            throw new NotSupportedException();
        }

        public override void UnregisterPropertyChangedEventHandler(PropertyChangedEventHandlerBase eventHandler)
        {
            throw new NotSupportedException();
        }

        public override void UnregisterStructureChangedEventHandler(StructureChangedEventHandlerBase eventHandler)
        {
            throw new NotSupportedException();
        }

        public override void UnregisterNotificationEventHandler(NotificationEventHandlerBase eventHandler)
        {
            throw new NotSupportedException();
        }

        public override void UnregisterTextEditTextChangedEventHandler(TextEditTextChangedEventHandlerBase eventHandler)
        {
            throw new NotSupportedException();
        }

        public override PatternId[] GetSupportedPatterns()
        {
            return new PatternId[0];
        }

        public override PropertyId[] GetSupportedProperties()
        {
            throw new NotSupportedException();
        }

        public override AutomationElement GetUpdatedCache()
        {
            throw new NotSupportedException();
        }

        public override AutomationElement[] GetCachedChildren()
        {
            throw new NotSupportedException();
        }

        public override AutomationElement GetCachedParent()
        {
            throw new NotSupportedException();
        }

        public override object GetCurrentMetadataValue(PropertyId targetId, int metadataId)
        {
            throw new NotSupportedException();
        }

        protected override IAutomationPattern<IAnnotationPattern> InitializeAnnotationPattern()
        {
            return new NoPattern<IAnnotationPattern>();
        }

        protected override IAutomationPattern<IDockPattern> InitializeDockPattern()
        {
            return new NoPattern<IDockPattern>();
        }

        protected override IAutomationPattern<IDragPattern> InitializeDragPattern()
        {
            return new NoPattern<IDragPattern>();
        }

        protected override IAutomationPattern<IDropTargetPattern> InitializeDropTargetPattern()
        {
            return new NoPattern<IDropTargetPattern>();
        }

        protected override IAutomationPattern<IExpandCollapsePattern> InitializeExpandCollapsePattern()
        {
            return new NoPattern<IExpandCollapsePattern>();
        }

        protected override IAutomationPattern<IGridItemPattern> InitializeGridItemPattern()
        {
            return new NoPattern<IGridItemPattern>();
        }

        protected override IAutomationPattern<IGridPattern> InitializeGridPattern()
        {
            return new NoPattern<IGridPattern>();
        }

        protected override IAutomationPattern<IInvokePattern> InitializeInvokePattern()
        {
            return new NoPattern<IInvokePattern>();
        }

        protected override IAutomationPattern<IItemContainerPattern> InitializeItemContainerPattern()
        {
            return new NoPattern<IItemContainerPattern>();
        }

        protected override IAutomationPattern<ILegacyIAccessiblePattern> InitializeLegacyIAccessiblePattern()
        {
            return new NoPattern<ILegacyIAccessiblePattern>();
        }

        protected override IAutomationPattern<IMultipleViewPattern> InitializeMultipleViewPattern()
        {
            return new NoPattern<IMultipleViewPattern>();
        }

        protected override IAutomationPattern<IObjectModelPattern> InitializeObjectModelPattern()
        {
            return new NoPattern<IObjectModelPattern>();
        }

        protected override IAutomationPattern<IRangeValuePattern> InitializeRangeValuePattern()
        {
            return new NoPattern<IRangeValuePattern>();
        }

        protected override IAutomationPattern<IScrollItemPattern> InitializeScrollItemPattern()
        {
            return new NoPattern<IScrollItemPattern>();
        }

        protected override IAutomationPattern<IScrollPattern> InitializeScrollPattern()
        {
            return new NoPattern<IScrollPattern>();
        }

        protected override IAutomationPattern<ISelectionItemPattern> InitializeSelectionItemPattern()
        {
            return new NoPattern<ISelectionItemPattern>();
        }

        protected override IAutomationPattern<ISelection2Pattern> InitializeSelection2Pattern()
        {
            return new NoPattern<ISelection2Pattern>();
        }

        protected override IAutomationPattern<ISelectionPattern> InitializeSelectionPattern()
        {
            return new NoPattern<ISelectionPattern>();
        }

        protected override IAutomationPattern<ISpreadsheetItemPattern> InitializeSpreadsheetItemPattern()
        {
            return new NoPattern<ISpreadsheetItemPattern>();
        }

        protected override IAutomationPattern<ISpreadsheetPattern> InitializeSpreadsheetPattern()
        {
            return new NoPattern<ISpreadsheetPattern>();
        }

        protected override IAutomationPattern<IStylesPattern> InitializeStylesPattern()
        {
            return new NoPattern<IStylesPattern>();
        }

        protected override IAutomationPattern<ISynchronizedInputPattern> InitializeSynchronizedInputPattern()
        {
            return new NoPattern<ISynchronizedInputPattern>();
        }

        protected override IAutomationPattern<ITableItemPattern> InitializeTableItemPattern()
        {
            return new NoPattern<ITableItemPattern>();
        }

        protected override IAutomationPattern<ITablePattern> InitializeTablePattern()
        {
            return new NoPattern<ITablePattern>();
        }

        protected override IAutomationPattern<ITextChildPattern> InitializeTextChildPattern()
        {
            return new NoPattern<ITextChildPattern>();
        }

        protected override IAutomationPattern<ITextEditPattern> InitializeTextEditPattern()
        {
            return new NoPattern<ITextEditPattern>();
        }

        protected override IAutomationPattern<IText2Pattern> InitializeText2Pattern()
        {
            return new NoPattern<IText2Pattern>();
        }

        protected override IAutomationPattern<ITextPattern> InitializeTextPattern()
        {
            return new NoPattern<ITextPattern>();
        }

        protected override IAutomationPattern<ITogglePattern> InitializeTogglePattern()
        {
            return new NoPattern<ITogglePattern>();
        }

        protected override IAutomationPattern<ITransform2Pattern> InitializeTransform2Pattern()
        {
            return new NoPattern<ITransform2Pattern>();
        }

        protected override IAutomationPattern<ITransformPattern> InitializeTransformPattern()
        {
            return new NoPattern<ITransformPattern>();
        }

        protected override IAutomationPattern<IValuePattern> InitializeValuePattern()
        {
            return SupportsValue ? (IAutomationPattern<IValuePattern>)new FakeValuePattern(this) : new NoPattern<IValuePattern>();
        }

        protected override IAutomationPattern<IVirtualizedItemPattern> InitializeVirtualizedItemPattern()
        {
            return new NoPattern<IVirtualizedItemPattern>();
        }

        protected override IAutomationPattern<IWindowPattern> InitializeWindowPattern()
        {
            return new NoPattern<IWindowPattern>();
        }
    }
}
