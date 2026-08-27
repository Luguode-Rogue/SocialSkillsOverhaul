using HarmonyLib;
using System;
using System.Reflection;
using TaleWorlds.MountAndBlade;


namespace SocialSkillsOverhaul
{
    public class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "com.mod.SocialSkillsOverhaul";
        private Harmony _harmony;

        /// <summary>
        /// NavalDLC 程序集是否可用（运行时检测，可选依赖）
        /// </summary>
        public static bool NavalDLCAvailable { get; private set; }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            // 运行时检测 NavalDLC 是否已安装（避免硬编码引用导致无 DLC 时崩溃）
            NavalDLCAvailable = IsNavalDLCAvailable();

            _harmony = new Harmony(HarmonyId);

            // PatchAll 仅扫描本程序集内不依赖 NavalDLC 的特性。
            // 注意：所有依赖 NavalDLC 类型的 HarmonyPatch 必须从编译期 typeof 改为运行时手动 Patch，
            // 否则 PatchAll 在反射读取特性构造函数时会因找不到 NavalDLC 程序集而 FileNotFoundException。
            _harmony.PatchAll();

            // 仅当 NavalDLC 存在时，才手动应用针对 NavalDLC 的补丁
            if (NavalDLCAvailable)
            {
                FriendlyFirePenetration.TryPatchNavalDecideCrushedThrough(_harmony);
            }

            Console.WriteLine("[FriendlyFirePenetration] Mod已加载" +
                              (NavalDLCAvailable ? " (含 NavalDLC 支持)" : " (未检测到 NavalDLC，跳过相关补丁)"));
        }

        /// <summary>
        /// 在已加载的程序集中查找 NavalDLC 关键类型，判断 DLC 是否可用
        /// </summary>
        private static bool IsNavalDLCAvailable()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType("NavalDLC.GameComponents.NavalDLCCustomAgentApplyDamageModel");
                if (type != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}