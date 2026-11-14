using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// The game runs at <see cref="Walker.TicksPerSecond"/> (60) frames a second, never faster: more brings nothing to a
    /// 2D adventure, and one frame per movement tick keeps every step the same size on screen.
    /// <list type="bullet">
    /// <item>Monitor at a multiple of 60 Hz (60, 120, 240): vertical sync every n-th refresh — even frames, no tearing.</item>
    /// <item>Any other refresh rate (75, 144, 165 Hz): no vertical sync, frame limiter at 60. Frames cannot line up
    /// evenly with such a monitor, so an occasional uneven frame is possible (adaptive sync removes it).</item>
    /// </list>
    /// The editor's Game view ignores vertical sync, so the limiter is always set as well.
    /// </summary>
    public static class FramePacing
    {
        public const int FrameRate = Walker.TicksPerSecond;

        /// <summary>Vertical sync interval for a monitor refresh rate: refresh / 60 when it is a whole multiple, else 0 (off).</summary>
        public static int VSyncInterval(double refreshRate)
        {
            if (refreshRate < 1)
            {
                return 0;
            }

            var multiple = (int)System.Math.Round(refreshRate / FrameRate);
            return multiple >= 1 && System.Math.Abs((refreshRate / multiple) - FrameRate) < 0.5 ? multiple : 0;
        }

        public static void Apply()
        {
            QualitySettings.vSyncCount = VSyncInterval(Screen.currentResolution.refreshRateRatio.value);
            Application.targetFrameRate = FrameRate;
        }
    }
}
