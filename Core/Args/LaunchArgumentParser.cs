namespace SharpKVM
{
    public sealed class LaunchArgumentParseResult
    {
        public bool AutoStartClientMode { get; init; }
        public string AutoServerIP { get; init; } = "";
    }

    public static class LaunchArgumentParser
    {
        public static LaunchArgumentParseResult Parse(string[] args)
        {
            bool autoStartClientMode = false;
            string autoServerIP = "";

            if (args == null || args.Length == 0)
            {
                return new LaunchArgumentParseResult
                {
                    AutoStartClientMode = autoStartClientMode,
                    AutoServerIP = autoServerIP
                };
            }

            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? string.Empty).Trim();

                if (a.Equals("client", System.StringComparison.OrdinalIgnoreCase))
                {
                    autoStartClientMode = true;
                    if (i + 1 < args.Length) autoServerIP = (args[i + 1] ?? string.Empty).Trim();
                }
                else if (a.Equals("--client", System.StringComparison.OrdinalIgnoreCase) || a.Equals("-c", System.StringComparison.OrdinalIgnoreCase))
                {
                    autoStartClientMode = true;
                    if (i + 1 < args.Length) autoServerIP = (args[i + 1] ?? string.Empty).Trim();
                }
                else if (a.StartsWith("--client=", System.StringComparison.OrdinalIgnoreCase))
                {
                    autoStartClientMode = true;
                    autoServerIP = a.Substring("--client=".Length).Trim();
                }
                else if (a.Equals("--server", System.StringComparison.OrdinalIgnoreCase) || a.Equals("-s", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) autoServerIP = (args[i + 1] ?? string.Empty).Trim();
                }
                else if (a.StartsWith("--server=", System.StringComparison.OrdinalIgnoreCase))
                {
                    autoServerIP = a.Substring("--server=".Length).Trim();
                }
            }

            return new LaunchArgumentParseResult
            {
                AutoStartClientMode = autoStartClientMode,
                AutoServerIP = autoServerIP
            };
        }
    }
}
