using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// A mouse gesture made of repeated strokes, fed with pointer positions while the button is held.
    /// Positions are in the pad's local space (UI units, origin at the pad's centre, y up).
    /// </summary>
    public abstract class Gesture
    {
        public int RequiredStrokes { get; }
        public int Strokes { get; private set; }

        public float Progress => RequiredStrokes <= 0 ? 1f : Mathf.Clamp01((float)Strokes / RequiredStrokes);
        public bool IsComplete => Strokes >= RequiredStrokes;

        protected Gesture(int requiredStrokes)
        {
            RequiredStrokes = Mathf.Max(1, requiredStrokes);
        }

        /// <summary>Pointer pressed.</summary>
        public void Begin(Vector2 position)
        {
            OnBegin(position);
        }

        /// <summary>Pointer moved while held. Returns true when a stroke was completed.</summary>
        public bool Move(Vector2 position)
        {
            if(IsComplete || !OnMove(position)) return false;
            Strokes++;
            return true;
        }

        public void End()
        {
            OnEnd();
        }

        protected abstract void OnBegin(Vector2 position);
        protected abstract bool OnMove(Vector2 position);
        protected virtual void OnEnd() { }
    }

    /// <summary>
    /// Back-and-forth along one axis: every change of direction after travelling at least
    /// <see cref="MinTravel"/> is a stroke. Slicing is vertical; shaking the wok is horizontal.
    /// </summary>
    public class BackAndForthGesture : Gesture
    {
        public readonly bool Vertical;
        public readonly float MinTravel;

        private float extreme;
        private int direction;
        private bool active;

        public BackAndForthGesture(bool vertical, int requiredStrokes, float minTravel = 70f) : base(requiredStrokes)
        {
            Vertical = vertical;
            MinTravel = minTravel;
        }

        private float Axis(Vector2 position) => Vertical ? position.y : position.x;

        protected override void OnBegin(Vector2 position)
        {
            extreme = Axis(position);
            direction = 0;
            active = true;
        }

        protected override bool OnMove(Vector2 position)
        {
            if(!active) OnBegin(position);

            float value = Axis(position);
            float delta = value - extreme;

            // Still going the same way: push the turning point further out.
            if(direction != 0 && Mathf.Sign(delta) == direction)
            {
                extreme = value;
                return false;
            }

            if(Mathf.Abs(delta) < MinTravel) return false;

            direction = delta > 0f ? 1 : -1;
            extreme = value;
            return true;
        }

        protected override void OnEnd()
        {
            active = false;
        }
    }

    /// <summary>
    /// Left-to-right swipes. Travelling <see cref="MinTravel"/> to the right is a stroke; moving left
    /// earns nothing and starts the next stroke from wherever the pointer turns back.
    /// </summary>
    public class SwipeRightGesture : Gesture
    {
        public readonly float MinTravel;

        /// <summary>Leftward wobble (UI units) tolerated before a stroke restarts.</summary>
        private const float BackTolerance = 6f;

        private float strokeStart;
        private float peak;
        private bool active;

        public SwipeRightGesture(int requiredStrokes, float minTravel = 110f) : base(requiredStrokes)
        {
            MinTravel = minTravel;
        }

        protected override void OnBegin(Vector2 position)
        {
            strokeStart = peak = position.x;
            active = true;
        }

        protected override bool OnMove(Vector2 position)
        {
            if(!active) OnBegin(position);

            float x = position.x;

            // Turning back to the left drops the partial stroke; the next one starts from here.
            if(peak - x > BackTolerance)
            {
                strokeStart = peak = x;
                return false;
            }

            peak = Mathf.Max(peak, x);
            if(x - strokeStart < MinTravel) return false;

            strokeStart = peak = x;
            return true;
        }

        protected override void OnEnd()
        {
            active = false;
        }
    }

    /// <summary>
    /// A half circle around the pad's centre, from the top to the bottom, on either side.
    /// Going from the bottom up does not count. Points too close to the centre are ignored.
    /// </summary>
    public class HalfCircleDownGesture : Gesture
    {
        public readonly float MinRadius;
        /// <summary>How close to straight up / straight down (degrees) the start and end must be.</summary>
        public readonly float EndTolerance;
        /// <summary>How much of the half circle (degrees) must actually be swept.</summary>
        public readonly float MinSweep;

        /// <summary>Largest angle change accepted between two pointer samples.</summary>
        private const float MaxStepDegrees = 60f;

        /// <summary>How far (degrees) the pointer may drift backwards before the stroke breaks.</summary>
        private const float ReverseToleranceDegrees = 10f;

        private bool armed;
        private float backtrack;
        private float lastAngle;
        private float swept;

        public HalfCircleDownGesture(int requiredStrokes, float minRadius = 45f, float endTolerance = 50f, float minSweep = 140f)
            : base(requiredStrokes)
        {
            MinRadius = minRadius;
            EndTolerance = endTolerance;
            MinSweep = minSweep;
        }

        private static float Angle(Vector2 position) => Mathf.Atan2(position.y, position.x) * Mathf.Rad2Deg;

        private bool NearTop(float angle) => Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) <= EndTolerance;
        private bool NearBottom(float angle) => Mathf.Abs(Mathf.DeltaAngle(angle, -90f)) <= EndTolerance;

        protected override void OnBegin(Vector2 position)
        {
            armed = false;
            swept = 0f;
            TryArm(position);
        }

        private void TryArm(Vector2 position)
        {
            if(position.magnitude < MinRadius) return;

            float angle = Angle(position);
            lastAngle = angle;

            if(NearTop(angle))
            {
                armed = true;
                swept = 0f;
                backtrack = 0f;
            }
        }

        protected override bool OnMove(Vector2 position)
        {
            if(position.magnitude < MinRadius) return false;

            float angle = Angle(position);

            if(!armed)
            {
                TryArm(position);
                return false;
            }

            float delta = Mathf.DeltaAngle(lastAngle, angle);
            lastAngle = angle;

            // A big jump means the pointer cut across the middle instead of going around it.
            if(Mathf.Abs(delta) > MaxStepDegrees)
            {
                armed = false;
                TryArm(position);
                return false;
            }

            // Going back the other way breaks the stroke. Small wobbles are tolerated, but slow
            // backtracking adds up.
            if(swept != 0f && Mathf.Sign(delta) != Mathf.Sign(swept))
            {
                backtrack += Mathf.Abs(delta);

                if(backtrack > ReverseToleranceDegrees)
                {
                    armed = false;
                    TryArm(position);
                    return false;
                }
            }
            else
            {
                backtrack = 0f;
            }

            swept += delta;

            if(Mathf.Abs(swept) >= MinSweep && NearBottom(angle))
            {
                armed = false;
                swept = 0f;
                return true;
            }

            return false;
        }

        protected override void OnEnd()
        {
            armed = false;
            swept = 0f;
        }
    }
}
