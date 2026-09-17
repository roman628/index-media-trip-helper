using System.Collections.Generic;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>The arithmetic behind drag-to-reorder and the scrolling section index, kept apart from the elements so it can be tested.</summary>
    public static class ListMath
    {
        /// <summary>
        /// Where a dragged row belongs. <paramref name="rows"/> are the (top, height) of every
        /// row in list order, the dragged one included. Returns the index the dragged row should
        /// have in the final list: before the first other row whose middle is below the pointer.
        /// </summary>
        public static int DropIndex(IReadOnlyList<(float top, float height)> rows, int dragged, float pointerY)
        {
            int index = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i == dragged) continue;
                if (pointerY < rows[i].top + rows[i].height / 2f) return index;
                index++;
            }
            return index;
        }

        /// <summary>Move an element of a list to a new index (the index it has after the move).</summary>
        public static void Move<T>(IList<T> list, int from, int to)
        {
            if (from < 0 || from >= list.Count) return;
            if (to < 0) to = 0;
            if (to > list.Count - 1) to = list.Count - 1;
            if (from == to) return;
            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
        }

        /// <summary>
        /// The section a scrolled document is "in": the last one whose top has passed the lead
        /// line just under the top edge. At the very bottom the last section wins even when it
        /// is too short to reach the line.
        /// </summary>
        public static int ActiveSection(IReadOnlyList<float> sectionTops, float scrollY, float lead = 80f, float maxScroll = float.MaxValue)
        {
            if (sectionTops == null || sectionTops.Count == 0) return -1;
            if (maxScroll > 0 && scrollY >= maxScroll - 1f) return sectionTops.Count - 1;
            int current = 0;
            for (int i = 0; i < sectionTops.Count; i++)
                if (sectionTops[i] <= scrollY + lead) current = i;
            return current;
        }
    }
}
