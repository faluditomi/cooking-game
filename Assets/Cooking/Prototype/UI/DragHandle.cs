using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Cooking.Prototype
{
    /// <summary>
    /// Press-and-hold pickup for a UI element (an ingredient pile, a plate, the pan, a prep tool).
    /// It only reports the start of a drag and plain clicks. The prototype follows the pointer itself
    /// from then on, so the drag survives this element being rebuilt or destroyed meanwhile.
    /// </summary>
    public class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        /// <summary>A drag started here, at this screen position.</summary>
        public Action<Vector2> Began;
        /// <summary>Pressed and released without dragging.</summary>
        public Action Clicked;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if(eventData.button == PointerEventData.InputButton.Left) Began?.Invoke(eventData.position);
        }

        // Needed for the event system to start a drag on this element at all.
        public void OnDrag(PointerEventData eventData) { }

        public void OnEndDrag(PointerEventData eventData) { }

        public void OnPointerClick(PointerEventData eventData)
        {
            if(eventData.button == PointerEventData.InputButton.Left && !eventData.dragging) Clicked?.Invoke();
        }
    }
}
