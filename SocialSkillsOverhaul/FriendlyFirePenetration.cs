using HarmonyLib;
using SandBox.GameComponents;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;
using TaleWorlds.MountAndBlade.Diamond;

namespace SocialSkillsOverhaul
{
    /// <summary>
    /// 友军攻击穿透Mod主类
    /// 功能：允许近战攻击穿透友军单位，攻击方不陷入硬直
    /// </summary>

    // =========================================================================
    // Patch 2: 修改近战命中回调 - 移除攻击者硬直
    // 目标：友军攻击时不设置攻击者眩晕时间
    // =========================================================================

    /// <summary>
    /// 修改 MeleeHitCallback 函数中的攻击者硬直设置
    /// 原始功能：友军伤害时设置攻击者眩晕时间
    /// 修改后：友军攻击时不设置眩晕，攻击动作不被打断
    /// </summary>
    // =========================================================================
    // Patch 2: 修改近战命中回调 - 移除攻击者硬直
    // 【修改】HarmonyPatch的Type数组中移除所有ref关键字
    //// =========================================================================
    [HarmonyPatch(typeof(Mission), "MeleeHitCallback")]
    public class Patch_MeleeHitCallback
    {
        // Postfix - 执行后
        [HarmonyPrefix]
        public static bool Prefix(
            ref AttackCollisionData collisionData,
            Agent attacker,
            Agent victim,
            GameEntity realHitEntity,
            ref float inOutMomentumRemaining,
            ref MeleeCollisionReaction colReaction,
            CrushThroughState crushThroughState,
            Vec3 blowDir,
            Vec3 swingDir,
            ref HitParticleResultData hitParticleResultData,
            bool crushedThroughWithoutAgentCollision)
        {
            if (attacker != null && victim != null && attacker.IsFriendOf(victim))
            {
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Mission), "OnAgentHitBlocked")]
    public class Patch_OnAgentHitBlocked
    {
        // Postfix - 执行后
        [HarmonyPrefix]
        public static bool Prefix(Agent affectedAgent, Agent affectorAgent, ref AttackCollisionData collisionData, Vec3 blowDirection, Vec3 swingDirection, bool isMissile)
        {
            if (affectedAgent != null && affectorAgent != null && affectedAgent.IsFriendOf(affectorAgent))
            {
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Mission), "GetDefendCollisionResults")]
    public class Patch_GetDefendCollisionResults
    {
        // Postfix - 执行后
        [HarmonyPrefix]
        public static bool Prefix(Agent attackerAgent, Agent defenderAgent, CombatCollisionResult collisionResult, int attackerWeaponSlotIndex, bool isAlternativeAttack, StrikeType strikeType, Agent.UsageDirection attackDirection, float collisionDistanceOnWeapon, float attackProgress, bool attackIsParried, bool isPassiveUsageHit, bool isHeavyAttack, ref float defenderStunPeriod, ref float attackerStunPeriod, ref bool crushedThrough)
        {
            if (attackerAgent != null && defenderAgent != null && attackerAgent.IsFriendOf(defenderAgent))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AgentApplyDamageModel), "CalculateDefaultRemainingMomentum")]
    public class Patch_CalculateDefaultRemainingMomentum
    {
        [HarmonyPrefix]
        // 【修改】__result需要ref才能修改返回值
        // in参数在Patch方法中当作普通值参数，不需要写in
        public static bool Prefix(ref float __result,
          float originalMomentum, Blow b, AttackCollisionData collisionData, Agent attacker, Agent victim, MissionWeapon attackerWeapon, bool isCrushThrough)
        {
            if (!FriendlyFirePenetrationConfig.EnablePenetration)
            {
                return true;
            }

            if (attacker != null && victim != null && attacker.IsFriendOf(victim))
            {
                float penetrationRetention = FriendlyFirePenetrationConfig.FriendlyPenetrationMomentumRetention;
                __result = originalMomentum * penetrationRetention;
                return false;

                if (__result < FriendlyFirePenetrationConfig.MinimumMomentumThreshold)
                {
                    __result = 0f;
                    return false;
                }
            }
            return true;
        }
    }

    // =========================================================================
    // Patch 1: NavalDLC 版本的 DecideCrushedThrough
    // 目标函数：NavalDLC.GameComponents.NavalDLCCustomAgentApplyDamageModel.DecideCrushedThrough
    //
    // 重要：NavalDLC 为【可选依赖】。为避免在无 NavalDLC 时因编译期引用
    //       (typeof(NavalDLCCustomAgentApplyDamageModel)) 导致 PatchAll 反射
    //       读取特性时抛出 FileNotFoundException，这里改用运行时反射定位目标
    //       类型并以手动 Patch 方式应用，仅在 DLC 存在时由 SubModule 调用。
    // =========================================================================

    /// <summary>
    /// NavalDLC 可选依赖的运行时补丁辅助类（与 SubModule.cs 中调用名一致）
    /// </summary>
    public static class FriendlyFirePenetration
    {
    /// <summary>
    /// NavalDLC 版本 DecideCrushedThrough 的 Postfix 逻辑（普通静态方法，非特性类）
    /// </summary>
    private static void NavalDecideCrushedThroughPostfix(ref bool __result, Agent attackerAgent, Agent defenderAgent,
                                  float totalAttackEnergy, Agent.UsageDirection attackDirection,
                                  StrikeType strikeType, WeaponComponentData defendItem, bool isPassiveUsage)
    {
        // 【配置检查】如果功能未启用，执行原始函数
        if (!FriendlyFirePenetrationConfig.EnableFriendlyShieldBreak)
        {
            return ; // 执行原始函数
        }

        // 【空值检查】
        if (attackerAgent == null || defenderAgent == null)
        {
            return ; // 执行原始函数
        }

        // 【玩家限定检查】如果设置为仅玩家生效，且攻击方是 AI，则执行原始函数
        if (FriendlyFirePenetrationConfig.PlayerOnly && !attackerAgent.IsHuman)
        {
            return ; // 执行原始函数
        }

        // =========================================================================
        // 【核心功能】友军攻击强制突破格挡
        // =========================================================================

        // 检查是否为友军关系
        bool isFriendlyFire = attackerAgent.IsFriendOf(defenderAgent);

        if (isFriendlyFire)
        {
            // 【可选】如果设置为仅盾牌突破，检查防御物品是否为盾牌
            if (!FriendlyFirePenetrationConfig.ForceBreakAllFriendlyHits)
            {
                if (defendItem == null || !defendItem.IsShield)
                {
                    return ; // 非盾牌，执行原始函数
                }
            }

            // 【强制突破】设置返回值为 true
            __result = true;

            // 【调试日志】
            if (FriendlyFirePenetrationConfig.EnableDebugLog)
            {
                string defendItemType = (defendItem != null) ?
                    (defendItem.IsShield ? "盾牌" : "武器") : "空手";
                Console.WriteLine($"[FFSB-Naval] 友军突破格挡：{attackerAgent.Name} -> {defenderAgent.Name}");
                Console.WriteLine($"[FFSB-Naval] 防御物品：{defendItemType}, 攻击能量：{totalAttackEnergy:F2}");
            }

            return ; // 跳过原始函数
        }

        // 非友军情况，执行原始函数
        return ;
    }

    /// <summary>
    /// 仅在检测到 NavalDLC 时由 SubModule 调用：运行时反射定位目标类型与方法，
    /// 手动应用 Postfix 补丁。找不到 DLC 或方法时安全跳过，不抛异常。
    /// </summary>
    internal static void TryPatchNavalDecideCrushedThrough(Harmony harmony)
    {
        try
        {
            Type navalType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("NavalDLC.GameComponents.NavalDLCCustomAgentApplyDamageModel");
                if (t != null)
                {
                    navalType = t;
                    break;
                }
            }

            if (navalType == null)
                return;

            // 目标方法签名：bool DecideCrushedThrough(Agent, Agent, float, Agent.UsageDirection, StrikeType, WeaponComponentData, bool)
            var method = navalType.GetMethod("DecideCrushedThrough",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] {
                    typeof(Agent), typeof(Agent), typeof(float),
                    typeof(Agent.UsageDirection), typeof(StrikeType),
                    typeof(WeaponComponentData), typeof(bool)
                },
                null);

            if (method == null)
                return;

            var postfix = new HarmonyMethod(typeof(FriendlyFirePenetration)
                .GetMethod(nameof(NavalDecideCrushedThroughPostfix),
                    BindingFlags.NonPublic | BindingFlags.Static));

            if (postfix == null)
                return;

            harmony.Patch(method, postfix: postfix);
        }
        catch (Exception ex)
        {
            // 任何异常都不应影响主流程，仅记录
            Console.WriteLine($"[FriendlyFirePenetration] 应用 NavalDLC 补丁失败，已跳过：{ex.Message}");
        }
    }
    }

    // =========================================================================
    // Patch 2: Sandbox 版本的 DecideCrushedThrough (战役模式)
    // 目标函数：SandBox.GameComponents.SandboxAgentApplyDamageModel.DecideCrushedThrough
    // =========================================================================

    /// <summary>
    /// 拦截战役模式的突破格挡判定
    /// 【前置补丁】在原始函数执行前检查，友军攻击直接返回 true
    /// </summary>
    [HarmonyPatch(typeof(SandboxAgentApplyDamageModel), "DecideCrushedThrough")]
    public class Patch_SandboxDecideCrushedThrough
    {
        /// <summary>
        /// 前置补丁 - 在原始函数执行前运行
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, Agent attackerAgent, Agent defenderAgent,
                                  float totalAttackEnergy, Agent.UsageDirection attackDirection,
                                  StrikeType strikeType, WeaponComponentData defendItem, bool isPassiveUsage)
        {
            // 【配置检查】
            if (!FriendlyFirePenetrationConfig.EnableFriendlyShieldBreak)
            {
                return ;
            }

            // 【空值检查】
            if (attackerAgent == null || defenderAgent == null)
            {
                return ;
            }

            // 【玩家限定检查】
            if (FriendlyFirePenetrationConfig.PlayerOnly && !attackerAgent.IsHuman)
            {
                return ;
            }

            // =========================================================================
            // 【核心功能】友军攻击强制突破格挡
            // =========================================================================

            bool isFriendlyFire = attackerAgent.IsFriendOf(defenderAgent);

            if (isFriendlyFire)
            {
                // 【盾牌检查】
                if (!FriendlyFirePenetrationConfig.ForceBreakAllFriendlyHits)
                {
                    if (defendItem == null || !defendItem.IsShield)
                    {
                        return ;
                    }
                }

                // 【强制突破】
                __result = true;

                // 【调试日志】
                if (FriendlyFirePenetrationConfig.EnableDebugLog)
                {
                    string defendItemType = (defendItem != null) ?
                        (defendItem.IsShield ? "盾牌" : "武器") : "空手";
                    Console.WriteLine($"[FFSB-Sandbox] 友军突破格挡：{attackerAgent.Name} -> {defenderAgent.Name}");
                    Console.WriteLine($"[FFSB-Sandbox] 防御物品：{defendItemType}");
                }

                return ; // 跳过原始函数
            }

            return ; // 执行原始函数
        }
    }

    // =========================================================================
    // Patch 3: 基础 Custom 版本的 DecideCrushedThrough (自定义战斗/多人)
    // 目标函数：TaleWorlds.MountAndBlade.CustomAgentApplyDamageModel.DecideCrushedThrough
    // =========================================================================

    /// <summary>
    /// 拦截自定义战斗/多人模式的突破格挡判定
    /// 【前置补丁】在原始函数执行前检查，友军攻击直接返回 true
    /// </summary>
    [HarmonyPatch(typeof(CustomAgentApplyDamageModel), "DecideCrushedThrough")]
    public class Patch_CustomDecideCrushedThrough
    {
        /// <summary>
        /// 后置补丁 - 在原始函数执行前运行
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(ref bool __result,Agent attackerAgent, Agent defenderAgent, float totalAttackEnergy, Agent.UsageDirection attackDirection, StrikeType strikeType, WeaponComponentData defendItem, bool isPassiveUsage)
        {
            // 【配置检查】
            if (!FriendlyFirePenetrationConfig.EnableFriendlyShieldBreak)
            {
                return ;
            }

            // 【空值检查】
            if (attackerAgent == null || defenderAgent == null)
            {
                return ;
            }

            // 【玩家限定检查】
            if (FriendlyFirePenetrationConfig.PlayerOnly && !attackerAgent.IsHuman)
            {
                return ;
            }

            // =========================================================================
            // 【核心功能】友军攻击强制突破格挡
            // =========================================================================

            bool isFriendlyFire = attackerAgent.IsFriendOf(defenderAgent);

            if (isFriendlyFire)
            {
                // 【盾牌检查】
                if (!FriendlyFirePenetrationConfig.ForceBreakAllFriendlyHits)
                {
                    if (defendItem == null || !defendItem.IsShield)
                    {
                        return ;
                    }
                }

                // 【强制突破】
                __result = true;

                // 【调试日志】
                if (FriendlyFirePenetrationConfig.EnableDebugLog)
                {
                    string defendItemType = (defendItem != null) ?
                        (defendItem.IsShield ? "盾牌" : "武器") : "空手";
                    Console.WriteLine($"[FFSB-Custom] 友军突破格挡：{attackerAgent.Name} -> {defenderAgent.Name}");
                    Console.WriteLine($"[FFSB-Custom] 防御物品：{defendItemType}");
                }

                return ; // 跳过原始函数
            }

            return ; // 执行原始函数
        }
    }

    // =========================================================================
    // 配置文件类
    // =========================================================================

    /// <summary>
    /// Mod配置类
    /// 所有可调参数集中管理，方便平衡调整
    /// </summary>
    public static class FriendlyFirePenetrationConfig
    {        /// <summary>
             /// 是否启用友军盾牌突破功能
             /// 默认：true
             /// </summary>
        public static bool EnableFriendlyShieldBreak = true;

        /// <summary>
        /// 友军盾牌突破时动量保留比例
        /// 默认：1.0f (100% 不减少)
        /// 原始值：0.3f (30%)
        /// </summary>
        public static float FriendlyBreakMomentumRetention = 1.0f;

        /// <summary>
        /// 是否移除攻击者硬直
        /// 默认：true
        /// </summary>
        public static bool RemoveAttackerStun = true;

        /// <summary>
        /// 是否仅对玩家生效 (AI 攻击友军不触发)
        /// 默认：false (玩家和 AI 都生效)
        /// </summary>
        public static bool PlayerOnly = false;

        /// <summary>
        /// 是否启用调试日志
        /// 默认：false
        /// </summary>
        public static bool EnableDebugLog = false;

        /// <summary>
        /// 是否对所有友军攻击强制突破 (不仅限于盾牌)
        /// 默认：false (仅盾牌)
        /// </summary>
        public static bool ForceBreakAllFriendlyHits = false;
        /// <summary>
        /// 是否启用友军穿透功能
        /// 默认：true
        /// </summary>
        public static bool EnablePenetration = true;

        /// <summary>
        /// 友军穿透时动量保留比例
        /// 原始值：0.3f (30%)
        /// 推荐值：0.5f - 0.7f (50%-70%)
        /// 说明：值越高，越容易连续穿透多个友军
        /// </summary>
        public static float FriendlyPenetrationMomentumRetention = 1f;

        /// <summary>
        /// 最小动量阈值
        /// 低于此值动量清零，无法继续穿透
        /// 默认：0.25f
        /// </summary>
        public static float MinimumMomentumThreshold = 0.0f;

        /// <summary>
        /// 是否减少友军攻击的AI警报
        /// 默认：true
        /// 说明：避免友军误伤触发大规模AI警觉
        /// </summary>
        public static bool ReduceFriendlyFireAlarm = true;

        /// <summary>
        /// 友军攻击警报倍率
        /// 默认：0.5f (降低50%警报范围)
        /// </summary>
        public static float FriendlyFireAlarmMultiplier = 0.5f;



        /// <summary>
        /// 是否保留友军伤害计算
        /// 默认：true
        /// 说明：false则友军攻击完全无伤害
        /// </summary>
        public static bool EnableFriendlyFireDamage = true;

        /// <summary>
        /// 友军伤害比例
        /// 默认：1.0f (100%伤害)
        /// 推荐：0.0f - 0.5f (0%-50%伤害)
        /// </summary>
        public static float FriendlyFireDamageMultiplier = 0.25f;
    }
}