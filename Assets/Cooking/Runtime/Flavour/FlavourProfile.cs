using System;
using System.Text;
using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// A point on the flavour radar: one value per <see cref="FlavourAxis"/>. Used for ingredient
    /// stats, processing deltas (which may be negative), finished dishes, and zone bounds.
    /// </summary>
    [Serializable]
    public struct FlavourProfile : IEquatable<FlavourProfile>
    {
        /// <summary>Top of the radar scale. Dish profiles are clamped to 0..MaxValue.</summary>
        public const float MaxValue = 10f;

        public float savoury;
        public float salty;
        public float sweet;
        public float bitter;
        public float sour;
        public float pain;
        public float nutrition;
        [Tooltip("0 = mushy, 10 = crunchy.")]
        public float texture;

        public static FlavourProfile Zero => default;

        /// <summary>A profile with every axis set to <paramref name="value"/>.</summary>
        public static FlavourProfile Filled(float value)
        {
            return new FlavourProfile
            {
                savoury = value,
                salty = value,
                sweet = value,
                bitter = value,
                sour = value,
                pain = value,
                nutrition = value,
                texture = value,
            };
        }

        public float this[FlavourAxis axis]
        {
            get
            {
                switch(axis)
                {
                    case FlavourAxis.Savoury: return savoury;
                    case FlavourAxis.Salty: return salty;
                    case FlavourAxis.Sweet: return sweet;
                    case FlavourAxis.Bitter: return bitter;
                    case FlavourAxis.Sour: return sour;
                    case FlavourAxis.Pain: return pain;
                    case FlavourAxis.Nutrition: return nutrition;
                    case FlavourAxis.Texture: return texture;
                    default: throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
                }
            }
            set
            {
                switch(axis)
                {
                    case FlavourAxis.Savoury: savoury = value; break;
                    case FlavourAxis.Salty: salty = value; break;
                    case FlavourAxis.Sweet: sweet = value; break;
                    case FlavourAxis.Bitter: bitter = value; break;
                    case FlavourAxis.Sour: sour = value; break;
                    case FlavourAxis.Pain: pain = value; break;
                    case FlavourAxis.Nutrition: nutrition = value; break;
                    case FlavourAxis.Texture: texture = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
                }
            }
        }

        /// <summary>Copy with every axis clamped to [min, max].</summary>
        public FlavourProfile Clamped(float min = 0f, float max = MaxValue)
        {
            FlavourProfile result = this;

            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                result[axis] = Mathf.Clamp(result[axis], min, max);
            }

            return result;
        }

        /// <summary>Euclidean distance between two profiles across all axes.</summary>
        public static float Distance(FlavourProfile a, FlavourProfile b)
        {
            float sum = 0f;

            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                float d = a[axis] - b[axis];
                sum += d * d;
            }

            return Mathf.Sqrt(sum);
        }

        public static FlavourProfile operator +(FlavourProfile a, FlavourProfile b)
        {
            FlavourProfile result = a;

            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                result[axis] += b[axis];
            }

            return result;
        }

        public static FlavourProfile operator -(FlavourProfile a, FlavourProfile b)
        {
            FlavourProfile result = a;

            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                result[axis] -= b[axis];
            }

            return result;
        }

        public static FlavourProfile operator *(FlavourProfile a, float scale)
        {
            FlavourProfile result = a;

            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                result[axis] *= scale;
            }

            return result;
        }

        public static FlavourProfile operator *(float scale, FlavourProfile a)
        {
            return a * scale;
        }

        public bool Equals(FlavourProfile other)
        {
            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                if(!this[axis].Equals(other[axis])) return false;
            }

            return true;
        }

        /// <summary>True when every axis differs by at most <paramref name="tolerance"/>.</summary>
        public bool Approximately(FlavourProfile other, float tolerance = 0.0001f)
        {
            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                if(Mathf.Abs(this[axis] - other[axis]) > tolerance) return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is FlavourProfile other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = 17;

            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                hash = hash * 31 + this[axis].GetHashCode();
            }

            return hash;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder("(");

            for(int i = 0; i < FlavourAxes.Count; i++)
            {
                FlavourAxis axis = FlavourAxes.All[i];
                if(i > 0) sb.Append(", ");
                sb.Append(FlavourAxes.Label(axis)).Append(' ').Append(this[axis].ToString("0.##"));
            }

            return sb.Append(')').ToString();
        }
    }
}
