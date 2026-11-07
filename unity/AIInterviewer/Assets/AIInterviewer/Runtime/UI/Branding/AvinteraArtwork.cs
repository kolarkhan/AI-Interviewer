using System.Collections.Generic;
using AIInterviewer.Core.Graphics;
using UnityEngine;

namespace AIInterviewer.UI.Branding
{
    /// <summary>
    /// Vector data of the Avintera splash (source: Assets/AIInterviewer/Branding/avintera-splash-animated.svg,
    /// viewBox 0 0 1080 1920). Path strings are copied verbatim from the SVG.
    /// </summary>
    public static class AvinteraArtwork
    {
        public const float ViewWidth = 1080f;
        public const float ViewHeight = 1920f;

        public static readonly Color Background = new Color32(0x0B, 0x10, 0x20, 0xFF);
        public static readonly Color Ink = new Color32(0xF5, 0xF7, 0xFA, 0xFF);
        public static readonly Color GradientTop = new Color32(0x35, 0xD6, 0xE8, 0xFF);
        public static readonly Color GradientBottom = new Color32(0x53, 0x67, 0xFF, 0xFF);

        // <g transform="translate(295.693 673.398) scale(0.80854)"> — head + voice bars
        public static readonly Vector2 MarkOffset = new Vector2(295.693f, 673.398f);
        public const float MarkScale = 0.80854f;

        // Head silhouette with the eye cut-out (fill-rule="evenodd"), in mark space.
        public const string HeadPath =
            "M186 422 L178 350 C132 318 108 272 110 218 C112 146 172 94 246 94 C318 94 362 144 366 202 C367 218 370 230 " +
            "376 240 L398 274 C401 280 398 285 391 286 L378 288 C380 300 378 316 374 326 C370 340 358 348 342 348 L324 348 " +
            "C314 348 308 356 308 366 L307 398 Z M205.22 275.40 A50.0 50.0 0 0 1 205.22 196.60 A12.0 12.0 0 0 1 219.99 " +
            "215.51 A26.0 26.0 0 0 0 219.99 256.49 A12.0 12.0 0 0 1 205.22 275.40 Z";

        /// <summary>Voice bars in mark space: x, y, width, height (corner radius = width / 2).</summary>
        public static readonly Rect[] Bars =
        {
            new Rect(408f, 267f, 30f, 66f),
            new Rect(446f, 230f, 30f, 140f),
            new Rect(484f, 255f, 30f, 90f),
        };

        // <ellipse cx="548.0" cy="882.0" rx="251.9" ry="212.2"> — radial halo (view space)
        public static readonly Vector2 GlowCenter = new Vector2(548f, 882f);
        public static readonly Vector2 GlowRadius = new Vector2(251.9f, 212.2f);

        // <path transform="translate(311.903 1170.600) scale(0.106849)"> — "Avintera" wordmark
        public static readonly Vector2 WordOffset = new Vector2(311.903f, 1170.6f);
        public const float WordScale = 0.106849f;

        public const string WordPath =
            "M18.0 0 266.5 -730.0H498.5L755.0 0H608.5L392.0 -633L442.5 -611.0H317.5L370.0 -633L160.0 0ZM192.0 -193.5 236.5 -313.5H532.5L577.0 -193.5Z " +
            "M921.0 0 751.0 -543H894.0L1056.5 0.0ZM962.0 0V-114H1108.5V0.0ZM1017.0 0 1157.0 -543H1291.0L1144.0 0Z " +
            "M1418.0 0V-543H1557.0V0ZM1342.0 -439V-543H1557.0V-439ZM1468.75 -608.0Q1428.0 -608 1408.5 -629.25Q1389.0 -650.5 1389.0 -683.5Q1389.0 -716.5 1408.5595238095239 -737.75Q1428.1190476190477 -759.0 1468.5 -759.0Q1509.5 -759.0 1528.75 -737.75Q1548.0 -716.5 1548.0 -683.5Q1548.0 -650.5 1528.75 -629.25Q1509.5 -608.0 1468.75 -608.0Z " +
            "M1713.0 0V-543H1823.0V-310H1813.0Q1813.0 -392.5 1834.75 -448.0Q1856.5 -503.5 1900.25 -531.75Q1944.0 -560 2008.5 -560.0H2014.5Q2112.0 -560 2162.0 -497.25Q2212.0 -434.5 2212.0 -310.5V0H2073.0V-323Q2073.0 -373 2044.25 -404.0Q2015.5 -435.0 1965.5 -435.0Q1914.5 -435.0 1883.25 -403.25Q1852.0 -371.5 1852.0 -319V0Z " +
            "M2592.5 7.0Q2517.5 7.0 2469.0 -12.5Q2420.5 -32.0 2396.75 -78.25Q2373.0 -124.5 2373.0 -203.5L2373.5 -696.0H2503.5L2503.0 -195Q2503.0 -155 2524.5 -133.5Q2546.0 -112 2586.0 -112H2671.0V7ZM2287.0 -441V-543H2671.0V-441Z " +
            "M3027.0 19Q2957.0 19 2904.5 -5.0Q2852.0 -29 2817.5 -69.25Q2783.0 -109.5 2765.5 -159.5Q2748.0 -209.5 2748.0 -262V-281Q2748.0 -335 2765.5 -385.25Q2783.0 -435.5 2817.25 -475.25Q2851.5 -515.0 2902.75 -538.5Q2954.0 -562 3021.0 -562Q3109.0 -562 3168.25 -523.25Q3227.5 -484.5 3257.75 -422.25Q3288.0 -360 3288.0 -288V-237.5H2807.0V-322.5H3200.5L3158.0 -281Q3158.0 -333 3142.75 -370.0Q3127.5 -407.0 3097.25 -427.0Q3067.0 -447 3021.0 -447Q2974.5 -447.0 2942.5 -425.75Q2910.5 -404.5 2894.25 -365.25Q2878.0 -326 2878.0 -271Q2878.0 -219.5 2893.75 -180.0Q2909.5 -140.5 2942.5 -118.25Q2975.5 -96.0 3027.0 -96Q3077.5 -96.0 3109.5 -116.5Q3141.5 -137.0 3150.5 -166.5H3278.5Q3267.0 -111 3232.75 -69.0Q3198.5 -27.0 3146.25 -4.0Q3094.0 19 3027.0 19Z " +
            "M3400.0 0V-543H3510.0V-313H3507.0Q3507.0 -429.5 3557.0 -489.75Q3607.0 -550 3704.0 -550H3724.0V-429H3686.0Q3615.5 -429.0 3577.25 -391.25Q3539.0 -353.5 3539.0 -282.5V0Z " +
            "M4141.5 0.0V-161.0H4118.5V-340.0Q4118.5 -386.5 4095.5 -409.75Q4072.5 -433.0 4025.0 -433Q4000.0 -433 3965.0 -432.0Q3930.0 -431 3894.5 -429.5Q3859.0 -428 3831.0 -426V-544Q3854.0 -546 3882.75 -547.75Q3911.5 -549.5 3942.25 -550.25Q3973.0 -551 4000.0 -551Q4083.5 -551.0 4139.25 -528.75Q4195.0 -506.5 4223.25 -459.5Q4251.5 -412.5 4251.5 -336.5V0.0ZM3966.5 14.0Q3907.5 14.0 3863.25 -6.75Q3819.0 -27.5 3794.5 -66.75Q3770.0 -106 3770.0 -161Q3770.0 -220.5 3799.25 -258.75Q3828.5 -297.0 3882.0 -315.75Q3935.5 -334.5 4007.5 -334.5H4133.5V-251.5H4005.5Q3958.0 -251.5 3932.5 -228.25Q3907.0 -205 3907.0 -167.5Q3907.0 -130.5 3932.5 -107.75Q3958.0 -85 4005.5 -85.0Q4034.5 -85.0 4059.25 -95.5Q4084.0 -106 4100.25 -131.25Q4116.5 -156.5 4118.5 -201.0L4152.5 -162.0Q4147.5 -105.0 4125.0 -66.0Q4102.5 -27.0 4063.0 -6.5Q4023.5 14.0 3966.5 14.0Z";

        private static List<PathCommand> _head, _word;

        public static List<PathCommand> Head => _head ?? (_head = SvgPath.Parse(HeadPath));
        public static List<PathCommand> Word => _word ?? (_word = SvgPath.Parse(WordPath));
    }
}
