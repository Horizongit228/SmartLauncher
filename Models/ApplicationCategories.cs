namespace SmartLauncher.UI.Models
{
    public static class ApplicationCategories
    {
        public const string Development = "Разработка";
        public const string Games = "Игры";
        public const string Browsers = "Браузеры";
        public const string Communication = "Общение";
        public const string Multimedia = "Мультимедиа";
        public const string Work = "Работа";
        public const string Design = "Графика и дизайн";
        public const string Files = "Файлы и архивы";
        public const string System = "Система и утилиты";
        public const string Security = "Безопасность";
        public const string Education = "Образование";
        public const string Other = "Другое";

        public static IReadOnlyList<string> All { get; } =
            new[]
            {
                Development,
                Games,
                Browsers,
                Communication,
                Multimedia,
                Work,
                Design,
                Files,
                System,
                Security,
                Education,
                Other
            };

        public static string Infer(
            string name,
            string path = "")
        {
            string value =
                (name + " " + path)
                .ToLowerInvariant();

            if (ContainsAny(
                    value,
                    "steam",
                    "epic games",
                    "epicgames",
                    "gog",
                    "ubisoft",
                    "uplay",
                    "battle.net",
                    "battlenet",
                    "riot client",
                    "rockstar games",
                    "ea app",
                    "origin games",
                    "xbox",
                    "minecraft",
                    "game launcher",
                    "gaming",
                    "simulator",
                    "solitaire",
                    "sons of the forest",
                    "universe sandbox",
                    "симулятор"))
            {
                return Games;
            }

            if (ContainsAny(
                    value,
                    "visual studio",
                    "vscode",
                    "code.exe",
                    "jetbrains",
                    "rider",
                    "pycharm",
                    "webstorm",
                    "clion",
                    "datagrip",
                    "android studio",
                    "github desktop",
                    "git bash",
                    "git cmd",
                    "git gui",
                    "\\git\\",
                    "gitkraken",
                    "docker desktop",
                    "postman",
                    "insomnia",
                    "powershell",
                    "windows terminal",
                    "терминал",
                    "command prompt",
                    "командная строка",
                    "github cli",
                    "python",
                    "node.js",
                    "openjdk",
                    "java(tm)",
                    "idle (python",
                    "inno setup",
                    "putty",
                    "psftp",
                    "codex",
                    "unity hub",
                    "unreal editor",
                    "godot",
                    "unity ",
                    "unity.exe",
                    "unitybugreporter",
                    "sublime text",
                    "notepad++",
                    "google ai studio",
                    "devenv.exe"))
            {
                return Development;
            }

            if (ContainsAny(
                    value,
                    "google chrome",
                    "chrome.exe",
                    "microsoft edge",
                    "msedge",
                    "firefox",
                    "opera",
                    "vivaldi",
                    "brave",
                    "yandex browser",
                    "\\yandex\\",
                    "browser.exe",
                    "internet explorer",
                    "tor browser"))
            {
                return Browsers;
            }

            if (ContainsAny(
                    value,
                    "telegram",
                    "discord",
                    "slack",
                    "microsoft teams",
                    "whatsapp",
                    "viber",
                    "signal",
                    "skype",
                    "zoom",
                    "mts link",
                    "webex",
                    "grok",
                    "messenger"))
            {
                return Communication;
            }

            if (ContainsAny(
                    value,
                    "photoshop",
                    "illustrator",
                    "lightroom",
                    "figma",
                    "blender",
                    "gimp",
                    "inkscape",
                    "krita",
                    "paint.net",
                    "microsoft paint",
                    "paint 3d",
                    "mspaint",
                    "paint",
                    "coreldraw",
                    "davinci resolve",
                    "autocad",
                    "sketchup"))
            {
                return Design;
            }

            if (ContainsAny(
                    value,
                    "youtube",
                    "новости",
                    "spotify",
                    "vlc",
                    "foobar",
                    "winamp",
                    "music",
                    "media player",
                    "kmplayer",
                    "potplayer",
                    "obs studio",
                    "clipchamp",
                    "audacity",
                    "itunes",
                    "camera",
                    "фотографии",
                    "кино и тв"))
            {
                return Multimedia;
            }

            if (ContainsAny(
                    value,
                    "microsoft word",
                    "microsoft excel",
                    "powerpoint",
                    "libreoffice",
                    "openoffice",
                    "microsoft 365",
                    "office",
                    "outlook",
                    "thunderbird",
                    "windows mail",
                    "notion",
                    "obsidian",
                    "chatgpt",
                    "claude",
                    "evernote",
                    "onenote",
                    "acrobat",
                    "pdf",
                    "calendar",
                    "microsoft to do",
                    "power automate",
                    "onedrive",
                    "почта"))
            {
                return Work;
            }

            if (ContainsAny(
                    value,
                    "7-zip",
                    "7zip",
                    "winrar",
                    "peazip",
                    "nanazip",
                    "total commander",
                    "freecommander",
                    "onecommander",
                    "explorer++",
                    "file manager",
                    "alcohol 120",
                    "ultraiso",
                    "daemon tools",
                    "file recovery",
                    "winscp",
                    "torrent",
                    "архиватор"))
            {
                return Files;
            }

            if (ContainsAny(
                    value,
                    "kaspersky",
                    "avast",
                    "avg antivirus",
                    "eset",
                    "malwarebytes",
                    "bitdefender",
                    "norton",
                    "mcafee",
                    "windows security",
                    "безопасность windows",
                    "antivirus",
                    "antimalware",
                    "vpn"))
            {
                return Security;
            }

            if (ContainsAny(
                    value,
                    "anki",
                    "duolingo",
                    "moodle",
                    "scratch",
                    "geogebra",
                    "matlab",
                    "wolfram",
                    "dictionary",
                    "учебник",
                    "образование"))
            {
                return Education;
            }

            if (ContainsAny(
                    value,
                    "settings",
                    "настройки",
                    "параметры",
                    "архивация windows",
                    "блокнот",
                    "быстрая поддержка",
                    "действие щелчком",
                    "записки",
                    "карты",
                    "ножницы",
                    "погода",
                    "связь с телефоном",
                    "средство 3d-просмотра",
                    "техническая поддержка",
                    "центр отзывов",
                    "часы",
                    "sound recorder",
                    "звукозапись",
                    "mixed reality",
                    "смешанной реальности",
                    "calculator",
                    "калькулятор",
                    "microsoft store",
                    "administrative tools",
                    "character map",
                    "configure java",
                    "disk cleanup",
                    "family",
                    "hyper-v manager",
                    "iscsi initiator",
                    "livecaptions",
                    "magnify",
                    "memory diagnostics",
                    "narrator",
                    "odbc data sources",
                    "on-screen keyboard",
                    "pc health check",
                    "recoverydrive",
                    "registry editor",
                    "remote desktop connection",
                    "resource monitor",
                    "windows installation assistant",
                    "inspectvhd",
                    "hyper-v",
                    "dfrgui",
                    "steps recorder",
                    "system configuration",
                    "system information",
                    "task manager",
                    "voiceaccess",
                    "smart launcher",
                    "realtek audio",
                    "fluentflyout",
                    "nvidia",
                    "radeon",
                    "amd software",
                    "intel graphics",
                    "logitech",
                    "razer",
                    "powertoys",
                    "everything",
                    "hwinfo",
                    "cpu-z",
                    "gpu-z",
                    "crystaldisk",
                    "recuva",
                    "utility",
                    "control panel",
                    "диспетчер задач",
                    "проводник"))
            {
                return System;
            }

            return Other;
        }

        private static bool ContainsAny(
            string value,
            params string[] terms) =>
            terms.Any(term =>
                value.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase));
    }
}
