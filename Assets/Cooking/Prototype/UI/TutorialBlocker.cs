using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Cooking.Prototype
{
    /// <summary>
    /// A see-through sheet over the whole screen during the tutorial. It swallows every click except
    /// over its <see cref="Holes"/> (what the current step wants used), which pass straight through
    /// to whatever is underneath.
    /// </summary>
    public class TutorialBlocker : MonoBehaviour, ICanvasRaycastFilter, IPointerDownHandler
    {
        /// <summary>Where clicks still reach the game this step.</summary>
        public readonly List<RectTransform> Holes = new List<RectTransform>();
        /// <summary>A click landed somewhere the step doesn't use.</summary>
        public Action Blocked;

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            foreach(RectTransform hole in Holes)
            {
                if(hole != null && hole.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(hole, screenPoint, eventCamera)) return false;
            }

            return true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Blocked?.Invoke();
        }
    }
}
