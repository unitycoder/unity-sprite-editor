using System;
using System.Collections.Generic;

namespace BetterSpriteEditor.Operations.Slicing
{
    /// <summary>One texture per editor update. Cancellation leaves completed saves intact.</summary>
    internal sealed class BatchSliceQueue
    {
        private readonly List<BatchSliceItem> items;
        private readonly Action<BatchSliceItem> process;
        internal int Completed { get; private set; }
        internal int Total => items.Count;
        internal bool IsRunning { get; private set; }

        internal BatchSliceQueue(IEnumerable<BatchSliceItem> items, Action<BatchSliceItem> process)
        {
            this.items = new List<BatchSliceItem>(items);
            this.process = process;
            IsRunning = this.items.Count > 0;
        }

        internal void Step()
        {
            if (!IsRunning) return;
            var item = items[Completed++];
            try { process(item); }
            catch (Exception exception) { item.Status = BatchSliceStatus.Error; item.Message = exception.Message; }
            if (Completed == Total) IsRunning = false;
        }

        internal void Cancel() => IsRunning = false;
    }
}
