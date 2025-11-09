// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.UI.ImGuiControls
{
    /// <summary>
    /// ImGui 界面的翻译文本管理类
    /// </summary>
    public static class ImGuiTranslations
    {
        private static readonly Dictionary<string, string> _translations = new Dictionary<string, string>
        {
            // Assistant Window
            ["Legion Assistant"] = "军团助手",
            ["General"] = "常规",
            ["Agents"] = "代理",
            ["Organizer"] = "整理器",
            ["Filters"] = "过滤器",
            ["Item Database"] = "物品数据库",
            ["No tabs available"] = "没有可用的标签页",
            
            // Script Manager Window
            ["Script Manager"] = "脚本管理器",
            ["Menu"] = "菜单",
            ["Add +"] = "添加 +",
            ["Refresh"] = "刷新",
            ["Public Script Browser"] = "公共脚本浏览器",
            ["Script Recording"] = "脚本录制",
            ["Scripting Info"] = "脚本信息",
            ["Persistent Variables"] = "持久变量",
            ["Running Scripts"] = "运行中的脚本",
            ["Disable Module Cache"] = "禁用模块缓存",
            ["Scripts"] = "脚本",
            ["No group"] = "无分组",
            ["Play"] = "播放",
            ["Stop"] = "停止",
            ["Autostart: All characters"] = "自动启动：所有角色",
            ["Autostart: This character"] = "自动启动：当前角色",
            ["Moving: "] = "移动：",
            
            // Context Menu
            ["New Script"] = "新建脚本",
            ["New Group"] = "新建分组",
            ["Rename"] = "重命名",
            ["Delete"] = "删除",
            ["Edit"] = "编辑",
            ["Open Folder"] = "打开文件夹",
            ["Autostart (Global)"] = "自动启动（全局）",
            ["Autostart (Character)"] = "自动启动（角色）",
            ["Remove Autostart"] = "移除自动启动",
            
            // Dialogs
            ["Delete Script"] = "删除脚本",
            ["Delete Group"] = "删除分组",
            ["Are you sure you want to delete"] = "确定要删除吗",
            ["This action cannot be undone."] = "此操作无法撤销。",
            ["This will permanently delete the folder and ALL scripts inside it."] = "这将永久删除文件夹及其中的所有脚本。",
            ["Yes"] = "是",
            ["No"] = "否",
            ["Cancel"] = "取消",
            ["OK"] = "确定",
            ["Create"] = "创建",
            ["Enter name:"] = "输入名称：",
            ["Script name:"] = "脚本名称：",
            ["Group name:"] = "分组名称：",
            
            // Messages
            ["A file with the name"] = "已存在同名文件",
            ["already exists."] = "。",
            ["Error renaming script:"] = "重命名脚本时出错：",
            ["Error renaming group:"] = "重命名分组时出错：",
            ["Deleted script"] = "已删除脚本",
            ["Deleted group"] = "已删除分组",
            ["and all its contents"] = "及其所有内容",
            ["Access denied. Check directory permissions."] = "访问被拒绝。请检查目录权限。",
            ["Directory not found."] = "未找到目录。",
            ["Directory operation failed:"] = "目录操作失败：",
            ["Renamed group"] = "已重命名分组",
            ["to"] = "为",
            ["Source group"] = "源分组",
            ["not found."] = "未找到。",
            ["A group with the name"] = "已存在同名分组",
            
            // General Window
            ["Options"] = "选项",
            ["Info"] = "信息",
            ["HUD"] = "HUD",
            ["Spell Bar"] = "法术栏",
            ["Title Bar"] = "标题栏",
            ["Spell Indicators"] = "法术指示器",
            ["Friends List"] = "好友列表",
            ["Visual Config"] = "视觉配置",
            ["Delay Config"] = "延迟配置",
            ["Assistant Alpha"] = "助手透明度",
            ["Theme"] = "主题",
            ["Highlight game objects"] = "高亮游戏对象",
            ["Show Names"] = "显示名称",
            ["Auto open own corpse"] = "自动打开自己的尸体",
            ["Turn Delay"] = "转身延迟",
            ["Object Delay"] = "对象延迟",
            ["Profile not loaded"] = "配置文件未加载",
            ["Adjust the background transparency of all ImGui windows."] = "调整所有 ImGui 窗口的背景透明度。",
            ["Select the color theme for ImGui windows."] = "选择 ImGui 窗口的颜色主题。",
            ["Toggle the display of names above characters and NPCs in the game world."] = "切换游戏世界中角色和 NPC 头顶名称的显示。",
            ["Automatically open your own corpse when you die, even if auto open corpses is disabled."] = "当你死亡时自动打开自己的尸体，即使禁用了自动打开尸体功能。",
            ["Ping: "] = "延迟：",
            ["FPS: "] = "帧率：",
            ["Last Object: "] = "最后对象：",
            ["TazUO Version: "] = "TazUO 版本：",
            ["Copied last object to clipboard."] = "已复制最后对象到剪贴板。",
        };

        /// <summary>
        /// 获取翻译文本，如果没有翻译则返回原文
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;
                
            return _translations.TryGetValue(key, out string translation) ? translation : key;
        }

        /// <summary>
        /// 格式化翻译文本（支持参数）
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            string text = Get(key);
            return args.Length > 0 ? string.Format(text, args) : text;
        }
    }
}
