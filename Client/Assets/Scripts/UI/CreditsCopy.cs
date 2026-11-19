using UnityEngine;

namespace PingTheKiller.Client.UI
{
    // S3-credits: the Credits and licences screen copy (docs/store/CREDITS.md). Every string of the screen is here. The full
    // Inter licence text is not copied into code: it is read from Resources/Fonts/OFL.txt (a TextAsset).
    public static class CreditsCopy
    {
        public const string Developer = "NIPAD";
        public const string Title = "Credits and licences", Button = "Credits", Back = "Back";
        public const string CreditsHeading = "Credits", LicencesHeading = "Licences";
        public const string OflResource = "Fonts/OFL";

        public static string[] Lines => new[]
        {
            "Ping the Killer",
            "Design and programming: " + Developer,
            "Characters: Robot Kyle and Rio",
            "Animations: Human Animations by Kevin Iglesias; Mixamo (Adobe)",
            "City environment: City 03 by Fries and Seagull",
            "Props: Explosive Device C4 and Defuse Kit by QAtmo",
            "Visual effects: Hovl Studio",
            "Shaders: Toon Shaders Pro",
            "Sound effects: Sci-Fi Weapons - Bullet Hell Sound Effects Pack",
            "Font: Inter by The Inter Project Authors (SIL Open Font License 1.1)",
            "Tween library: DOTween by Demigiant",
        };

        public const string InterNotice = "Inter. Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter). Licensed under the SIL Open Font License, Version 1.1. The full licence text follows.";
        public const string DoTweenNotice = "DOTween. Copyright (c) 2014-2026 Daniele Giardini, Demigiant. http://dotween.demigiant.com";

        // The full OFL text, or an empty string when the asset is missing.
        public static string OflText()
        {
            var asset = Resources.Load<TextAsset>(OflResource);
            return asset != null ? asset.text.Replace("\r\n", "\n").Trim() : string.Empty;
        }
    }
}
