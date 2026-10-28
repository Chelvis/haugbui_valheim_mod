using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>Rises 5 cm in one second and returns in the next, then repeats.</summary>
    internal sealed class MarkFloat : MonoBehaviour
    {
        public const float Travel = 0.05f;
        public const float Period = 2f;

        private float _baseY;

        public static void Attach(Transform mark)
        {
            var bob = mark.gameObject.AddComponent<MarkFloat>();
            bob._baseY = mark.localPosition.y;
        }

        /// <summary>Height added at <paramref name="seconds"/> into the loop. 0 at the ends, <see cref="Travel"/> at 1 second.</summary>
        public static float Lift(float seconds)
        {
            float u = Mathf.Repeat(seconds, Period) / Period;
            float wave = u < 0.5f ? u * 2f : (1f - u) * 2f;
            return wave * Travel;
        }

        private void Update()
        {
            Vector3 place = transform.localPosition;
            place.y = _baseY + Lift(Time.time);
            transform.localPosition = place;
        }
    }
}
