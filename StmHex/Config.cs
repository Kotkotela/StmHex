using System;
using System.IO;
using System.Text.Json;

namespace StmHex
{
    public class AppConfig
    {
        public string CubeProgrammerPath { get; set; } =
            @"C:\Program Files\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe";
        public string StlinkSerial { get; set; } = "";
        public string LastHexPath { get; set; } = "";

        private static string configPath = "config.json";

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    return JsonSerializer.Deserialize<AppConfig>(json);
                }
            }
            catch { }

            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(configPath, json);
            }
            catch { }
        }
    }
}