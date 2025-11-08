// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using ClassicUO.Game.UI.Gumps.GridHighLight;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Configuration
{
    internal static class ProfileManager
    {
        public static Profile CurrentProfile { get; private set; }
        public static string ProfilePath { get; private set; }

        private static string _rootPath;
        private static string RootPath
        {
            get
            {
                if (string.IsNullOrEmpty(_rootPath))
                {
                    if (string.IsNullOrWhiteSpace(Settings.GlobalSettings.ProfilesPath))
                    {
                        _rootPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles");
                    }
                    else
                    {
                        _rootPath = Settings.GlobalSettings.ProfilesPath;
                    }
                }

                return _rootPath;
            }
        }

        public static void Load(string servername, string username, string charactername)
        {
            string path = FileSystemHelper.CreateFolderIfNotExists(RootPath, username.Trim(), servername.Trim(), charactername.Trim());
            string fileToLoad = Path.Combine(path, "profile.json");

            ProfilePath = path;
            CurrentProfile = ConfigurationResolver.Load<Profile>(fileToLoad, ProfileJsonContext.DefaultToUse.Profile) ?? NewFromDefault();

            CurrentProfile.Username = username;
            CurrentProfile.ServerName = servername;
            CurrentProfile.CharacterName = charactername;

            if (CurrentProfile.GridHighlightSetup.Count == 0)
            {
                GridHighLightProfile.MigrateGridHighlightToSetup(CurrentProfile);
                ConfigurationResolver.Save(CurrentProfile, Path.Combine(ProfilePath, "profile.json"), ProfileJsonContext.DefaultToUse.Profile);
            }

            ValidateFields(CurrentProfile);
            
            // 自动应用DPI缩放（如果尚未手动配置）
            ApplyAutoDPIScaling(CurrentProfile);

            Client.Game?.SetVSync(CurrentProfile.EnableVSync);
        }

        public static void SetProfileAsDefault(Profile profile) => profile.SaveAs(RootPath, "default.json");

        public static Profile NewFromDefault() => ConfigurationResolver.Load<Profile>(Path.Combine(RootPath, "default.json"), ProfileJsonContext.DefaultToUse.Profile) ?? new Profile();

        private static void ValidateFields(Profile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(profile.ServerName))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.Username))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.CharacterName))
            {
                throw new InvalidDataException();
            }

            if (profile.WindowClientBounds.X < 600)
            {
                profile.WindowClientBounds = new Point(600, profile.WindowClientBounds.Y);
            }

            if (profile.WindowClientBounds.Y < 480)
            {
                profile.WindowClientBounds = new Point(profile.WindowClientBounds.X, 480);
            }
        }

        public static void UnLoadProfile() => CurrentProfile = null;

        /// <summary>
        /// 根据DPI自动应用全局缩放
        /// 只在用户未手动配置时自动设置
        /// </summary>
        private static void ApplyAutoDPIScaling(Profile profile)
        {
            if (profile == null)
                return;

            float dpiScale = CUOEnviroment.DPIScaleFactor;

            // 如果DPI缩放大于1.0，且用户还没有启用GlobalScaling，则自动启用
            if (dpiScale > 1.0f && !profile.GlobalScaling)
            {
                // 检查是否是首次加载（GlobalScale仍为默认值1.5）
                // 如果用户已经手动调整过，我们不覆盖
                if (profile.GlobalScale == 1.5f || profile.GlobalScale == 1.0f)
                {
                    profile.GlobalScaling = true;
                    profile.GlobalScale = dpiScale;
                    
                    Utility.Logging.Log.Trace($"Auto-enabled global scaling: {dpiScale:F2}x for high-DPI display");
                }
            }
        }
    }
}
