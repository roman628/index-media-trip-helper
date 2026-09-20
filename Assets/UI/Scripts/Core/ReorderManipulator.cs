using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    /// <summary>
    /// Drag-to-reorder for a vertical list inside a ScrollView. Rows carry the class
    /// <see cref="RowClass"/> and their id in userData; a drag starts on a child with
    /// <see cref="HandleClass"/>. While dragging the row moves among its siblings, the list
    /// scrolls when the pointer nears an edge, and on release the new id order is reported.
    ///
    /// The pointer-down is taken in the trickle-down phase on the ScrollView itself, before the
    /// ScrollView's own touch-scroll handler (which lives on its viewport) sees it, so a drag
    /// on a handle never turns into a scroll. A press that does not move is reported as a tap
    /// on the handle (used to offer Move up / Move down).
    /// </summary>
    public sealed class ReorderManipulator : Manipulator
    {
        public const string RowClass = "entry";
        public const string HandleClass = "hd";
        public const string DraggingClass = "drag";
        private const float TapSlop = 6f;
        private const float EdgeZone = 56f;
        private const float EdgeSpeed = 14f;

        private readonly Action<List<string>> _onReordered;
        private readonly Action<string, VisualElement> _onHandleTap;
        private VisualElement _row;
        private VisualElement _handle;
        private int _pointerId = -1;
        private Vector2 _start;
        private bool _moved;
        private List<string> _startOrder;

        public bool Dragging => _row != null;

        private readonly string _rowClass;
        private readonly string _handleClass;

        /// <param name="rowClass">Class of the rows this instance moves. Two instances with different classes can share one ScrollView (chapters, and the sections inside them).</param>
        public ReorderManipulator(Action<List<string>> onReordered, Action<string, VisualElement> onHandleTap = null, string rowClass = RowClass, string handleClass = HandleClass)
        {
            _onReordered = onReordered;
            _onHandleTap = onHandleTap;
            _rowClass = rowClass;
            _handleClass = handleClass;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove);
            target.RegisterCallback<PointerUpEvent>(OnUp);
            target.RegisterCallback<PointerCancelEvent>(OnCancel);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
            target.UnregisterCallback<PointerCancelEvent>(OnCancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        private static VisualElement Ancestor(VisualElement from, string cls, VisualElement stopAt)
        {
            for (var v = from; v != null && v != stopAt; v = v.parent)
                if (v.ClassListContains(cls)) return v;
            return null;
        }

        private List<VisualElement> Rows() =>
            _row?.parent == null ? new List<VisualElement>() : _row.parent.Children().Where(c => c.ClassListContains(_rowClass)).ToList();

        private static List<string> Ids(IEnumerable<VisualElement> rows) => rows.Select(r => r.userData as string).ToList();

        private void OnDown(PointerDownEvent e)
        {
            if (_row != null) return;
            var handle = Ancestor(e.target as VisualElement, _handleClass, target);
            if (handle == null) return;
            var row = Ancestor(handle, _rowClass, target);
            if (row == null) return;
            _row = row;
            _handle = handle;
            _pointerId = e.pointerId;
            _start = e.position;
            _moved = false;
            _startOrder = Ids(Rows());
            target.CapturePointer(_pointerId);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_row == null || e.pointerId != _pointerId) return;
            if (!_moved)
            {
                if (((Vector2)e.position - _start).magnitude < TapSlop) return;
                _moved = true;
                _row.AddToClassList(DraggingClass);
            }
            var rows = Rows();
            var me = rows.IndexOf(_row);
            if (me < 0) return;
            var bounds = rows.Select(r => (r.worldBound.yMin, r.worldBound.height)).ToList();
            var to = ListMath.DropIndex(bounds, me, e.position.y);
            if (to != me)
            {
                var others = rows.Where(r => r != _row).ToList();
                if (to >= others.Count) _row.PlaceInFront(others[others.Count - 1]);
                else _row.PlaceBehind(others[to]);
            }
            AutoScroll(e.position.y);
            e.StopPropagation();
        }

        private void AutoScroll(float pointerY)
        {
            if (!(target is ScrollView sv)) return;
            var view = sv.contentViewport.worldBound;
            float delta = 0;
            if (pointerY < view.yMin + EdgeZone) delta = -EdgeSpeed;
            else if (pointerY > view.yMax - EdgeZone) delta = EdgeSpeed;
            if (delta == 0) return;
            var max = Mathf.Max(0, sv.contentContainer.layout.height - view.height);
            var y = Mathf.Clamp(sv.scrollOffset.y + delta, 0, max);
            sv.scrollOffset = new Vector2(sv.scrollOffset.x, y);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_row == null || e.pointerId != _pointerId) return;
            Finish(commit: true);
            e.StopPropagation();
        }

        private void OnCancel(PointerCancelEvent e)
        {
            if (_row == null || e.pointerId != _pointerId) return;
            Finish(commit: false);
        }

        private void OnCaptureOut(PointerCaptureOutEvent e)
        {
            if (_row != null) Finish(commit: _moved);
        }

        private void Finish(bool commit)
        {
            var row = _row; var handle = _handle; var moved = _moved; var before = _startOrder;
            var pointer = _pointerId;
            _row = null; _handle = null; _pointerId = -1; _moved = false; _startOrder = null;
            if (target.HasPointerCapture(pointer)) target.ReleasePointer(pointer);
            row.RemoveFromClassList(DraggingClass);
            if (!moved)
            {
                if (commit) _onHandleTap?.Invoke(row.userData as string, handle);
                return;
            }
            var after = row.parent == null ? before : Ids(row.parent.Children().Where(c => c.ClassListContains(_rowClass)));
            if (commit && !after.SequenceEqual(before)) _onReordered?.Invoke(after);
            else if (!commit) _onReordered?.Invoke(before);
        }
    }
}
