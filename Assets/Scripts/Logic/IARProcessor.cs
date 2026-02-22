using UnityEngine;
using StateData.Role;
using LitJson;
using System.Text.RegularExpressions; // 引入正则库

public class IARProcessor : MonoSingleton<IARProcessor>
{
    // 1. 本地行动校验 (Action Check) - 保持不变
    public bool CheckActionValidity(string inputAction, RoleState currentState, out string failReason)
    {
        failReason = "";
        if (currentState.attributes.currentHealth <= 0)
        {
            failReason = "你已气绝身亡，无法动弹。";
            return false;
        }
        return true;
    }

    // 2. 确定性逻辑 (Deterministic Logic) - 保持不变
    public string ExecuteDeterministicLogic(string inputAction, RoleState currentState)
    {
        // 这里的逻辑依然有效，作为前置反馈
        return "[系统：行动已通过IAR校验，正在推演因果...]";
    }

    // 3. 【新增】AI 结果解析器 (Result Analyzer)
    // 返回值：去除指令后的纯净剧情文本
    public string AnalyzeAndApplyAIResult(string aiFullResponse, RoleState state)
    {
        // 使用正则表达式提取 <CMD>...</CMD> 之间的内容
        // (?s) 开启单行模式，确保能匹配换行符
        string pattern = @"<CMD>(.*?)</CMD>";
        Match match = Regex.Match(aiFullResponse, pattern, RegexOptions.Singleline);

        if (match.Success)
        {
            string jsonCmd = match.Groups[1].Value;
            try
            {
                ApplyCommandToState(jsonCmd, state);
                Debug.Log($"<color=cyan>[IAR] 提取并执行指令: {jsonCmd}</color>");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[IAR] 指令解析失败: {e.Message}");
            }

            // 将指令从显示的文本中剔除，不要让玩家看到 JSON
            return aiFullResponse.Replace(match.Value, "").Trim();
        }

        return aiFullResponse;
    }

    // 执行具体的数值变更
    private void ApplyCommandToState(string jsonStr, RoleState state)
    {
        JsonData data = JsonMapper.ToObject(jsonStr);

        // 处理 HP 变化
        if (data.Keys.Contains("hp"))
        {
            int val = (int)data["hp"];
            state.attributes.currentHealth += val;
            // 钳制数值，防止超过上限或低于0
            state.attributes.currentHealth = Mathf.Clamp(state.attributes.currentHealth, 0, state.attributes.maxHealth);
        }

        // 处理 MP 变化
        if (data.Keys.Contains("mp"))
        {
            int val = (int)data["mp"];
            state.attributes.currentMana += val;
            state.attributes.currentMana = Mathf.Clamp(state.attributes.currentMana, 0, state.attributes.maxMana);
        }

        // 处理 EXP 变化
        if (data.Keys.Contains("exp"))
        {
            int val = (int)data["exp"];
            state.attributes.currentExp += val;
        }
    }
}