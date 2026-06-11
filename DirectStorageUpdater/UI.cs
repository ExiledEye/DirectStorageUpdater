using System;

namespace DirectStorageUpdater
{
    internal static class UI
    {
        // ── Color palette ──────────────────────────────────────────────────────
        private const ConsoleColor C_TITLE = ConsoleColor.Cyan;
        private const ConsoleColor C_HEADER = ConsoleColor.DarkCyan;
        private const ConsoleColor C_OK = ConsoleColor.Green;
        private const ConsoleColor C_WARN = ConsoleColor.Yellow;
        private const ConsoleColor C_ERROR = ConsoleColor.Red;
        private const ConsoleColor C_INFO = ConsoleColor.White;
        private const ConsoleColor C_MUTED = ConsoleColor.DarkGray;
        private const ConsoleColor C_ACCENT = ConsoleColor.Magenta;
        private const ConsoleColor C_INPUT = ConsoleColor.DarkYellow;
        private const ConsoleColor C_LATEST = ConsoleColor.Green;
        private const ConsoleColor C_PREVIEW = ConsoleColor.DarkYellow;
        private const ConsoleColor C_VERSION = ConsoleColor.White;

        // ── Primitives ─────────────────────────────────────────────────────────
        public static void Write(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }

        public static void WriteLine(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        public static void WriteLine() => Console.WriteLine();

        // ── Semantic helpers ───────────────────────────────────────────────────
        public static void Title(string appVersion)
        {
            Console.Clear();
            Console.ForegroundColor = C_TITLE;
            Console.WriteLine("╔═════════════════════════════════════════════════════╗");
            Console.WriteLine($"║           DirectStorageUpdater  v{appVersion}              ║");
            Console.WriteLine("╚═════════════════════════════════════════════════════╝");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" Author: Exiled Eye         ");
            Link("GitHub", "https://github.com/ExiledEye/DirectStorageUpdater");
            Console.Write("          ");
            Link("Nexus Mods", "https://www.nexusmods.com/site/mods/1982");
            Console.WriteLine();
            WriteLine("───────────────────────────────────────────────────────", C_TITLE);
            Console.ResetColor();
            Console.WriteLine();
        }

        private static void Link(string text, string url)
        {
            Console.Write("\x1b[4m");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"\x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\");
            Console.Write("\x1b[24m");
            Console.ForegroundColor = C_TITLE;
        }

        public static void Section(string text)
        {
            Console.WriteLine();
            WriteLine($"── {text} ", C_HEADER);
        }

        public static void Ok(string text) => WriteLine($"  ✔  {text}", C_OK);
        public static void Warn(string text) => WriteLine($"  ⚠  {text}", C_WARN);
        public static void Error(string text) => WriteLine($"  ✖  {text}", C_ERROR);
        public static void Info(string text) => WriteLine($"     {text}", C_INFO);
        public static void Muted(string text) => WriteLine($"     {text}", C_MUTED);

        public static void VersionLine(string label, string version, bool isLatest = false, bool isPreview = false, string? fileVersion = null)
        {
            Write($"     {label,-20}", C_MUTED);
            ConsoleColor vc = isLatest ? C_LATEST : isPreview ? C_PREVIEW : C_VERSION;
            Write(version, vc);
            if (fileVersion is not null) Write($"  ({fileVersion})", C_MUTED);
            if (isLatest) Write("  ◄ latest stable", C_LATEST);
            if (isPreview) Write("  (preview)", C_PREVIEW);
            Console.WriteLine();
        }

        public static void Prompt(string text)
        {
            Write($"\n  ► {text}: ", C_INPUT);
        }

        public static void PromptInline(string text)
        {
            Write($"  ► {text} ", C_INPUT);
        }

        /// <summary>Numbered list item.</summary>
        public static void ListItem(int indexDisplay, string text,
                                    bool recommended = false, bool isPreview = false)
        {
            bool showRec = recommended && !isPreview;
            ConsoleColor numCol = showRec ? C_LATEST : isPreview ? C_PREVIEW : C_MUTED;
            ConsoleColor txtCol = showRec ? C_LATEST : isPreview ? C_PREVIEW : C_INFO;
            string suffix = showRec ? "  ◄ recommended" : isPreview ? "  ◄ preview, not recommended" : "";

            Write($"     [{indexDisplay}] ", numCol);
            Write(text, txtCol);
            WriteLine(suffix, showRec ? C_LATEST : C_PREVIEW);
        }

        public static void Divider()
        {
            WriteLine("     " + new string('─', 50), C_MUTED);
        }

        public static void Progress(long downloaded, long total, bool done = false)
        {
            const int width = 30;
            double ratio = total > 0 ? Math.Clamp((double)downloaded / total, 0, 1) : 0;
            int filled = (int)(width * ratio);
            string bar = new string('█', filled) + new string('░', width - filled);
            string pct = (ratio * 100).ToString("0");
            string dl = $"{downloaded / 1024.0 / 1024.0:0.00}";
            string tot = total > 0 ? $"/{total / 1024.0 / 1024.0:0.00} MB" : " MB";

            Console.CursorLeft = 0;
            Write($"     [{bar}] {pct,3}%  {dl}{tot}   ", done ? C_OK : C_ACCENT);
            if (done) Console.WriteLine();
        }

        public static bool AskYesNo(string question, bool defaultYes = true)
        {
            string hint = defaultYes ? "[Y/n]" : "[y/N]";
            PromptInline($"{question} {hint}");
            string? raw = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(raw)) return defaultYes;
            return raw is "y" or "yes";
        }

        public static int AskChoice(int count, int defaultIndex)
        {
            while (true)
            {
                Prompt($"Enter choice (1-{count}), default={defaultIndex + 1}");
                string? raw = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(raw)) return defaultIndex;
                if (int.TryParse(raw, out int n) && n >= 1 && n <= count)
                    return n - 1;
                Warn($"Invalid input. Enter a number between 1 and {count}.");
            }
        }

        public static int? AskChoiceOrSkip(int count, int? defaultIndex)
        {
            while (true)
            {
                string hint = defaultIndex.HasValue
                    ? $"Enter choice (1-{count}), default={defaultIndex.Value + 1}"
                    : $"Enter choice (1-{count}), or press Enter to skip";
                Prompt(hint);
                string? raw = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(raw))
                    return defaultIndex.HasValue ? defaultIndex.Value : null;
                if (int.TryParse(raw, out int n) && n >= 1 && n <= count)
                    return n - 1;
                Warn($"Invalid input. Enter a number between 1 and {count}.");
            }
        }

        public static void PressAnyKey()
        {
            Console.WriteLine();
            Write("  Press any key to exit...", C_MUTED);
            Console.ReadKey(intercept: true);
            Console.WriteLine();
        }

        // Changelog stuff
        public static void PrintChangelog(string version, string[] lines)
        {
            Console.WriteLine();
            Write("  ┌─ ", C_HEADER);
            Write($"v{version}", C_ACCENT);
            WriteLine(" " + new string('─', Math.Max(0, 48 - version.Length)), C_HEADER);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine();
                    continue;
                }

                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("####"))
                {
                    Write("  │  ", C_HEADER);
                    WriteLine(trimmed.TrimStart('#').Trim(), C_WARN);
                }
                else if (trimmed.StartsWith("###"))
                {
                    // sub-version headings
                    Write("  │  ", C_HEADER);
                    WriteLine(trimmed.TrimStart('#').Trim(), C_ACCENT);
                }
                else if (trimmed.StartsWith("*") || trimmed.StartsWith("-") || trimmed.StartsWith("+"))
                {
                    Write("  │    • ", C_MUTED);
                    WriteLine(trimmed.TrimStart('*', '-', '+').Trim(), C_INFO);
                }
                else if (trimmed.StartsWith("  ") || trimmed.StartsWith("\t"))
                {
                    Write("  │      ", C_MUTED);
                    WriteLine(trimmed, C_MUTED);
                }
                else
                {
                    Write("  │  ", C_HEADER);
                    WriteLine(trimmed, C_INFO);
                }
            }

            WriteLine("  └" + new string('─', 50), C_HEADER);
        }
    }
}
