// 司南工具箱 (WINHELP)
// Copyright (C) 2025-2026 YYRMM
// 本程序为自由软件，在 GNU 通用公共许可证第 2 版（GPL v2）下发布。
// 你可以自由使用、复制、修改和再分发，但须保留本协议且不附加任何限制。
// 本程序按“现状”提供，不含任何担保。详见 LICENSE。

using System.Text;
using System.Windows;

namespace WINHELP
{
    /// <summary>
    /// 高危风险操作守卫（v5.2.0 新增）。
    /// <para>
    /// 对磁盘修复、防火墙重置、文件粉碎、系统安全策略修改等可能造成不可逆后果的代码执行项，
    /// 要求用户连续确认 N 次（默认 5 次）：每一次弹窗都明确标注「高危风险行为警告 (i/N)」、
    /// 说明具体命令与影响，默认按钮为「否」，用户必须逐次点「是/继续」才能放行；
    /// 任一轮点「否/取消」立即中止，操作不会执行。
    /// </para>
    /// <para>设计取舍：不用一次大弹窗堆 5 段文字（用户容易扫一眼直接点确定），
    /// 而是 5 次独立确认，每次都要主动点击才能继续，从交互上强制“看清再点”。</para>
    /// </summary>
    public static class RiskGuard
    {
        /// <summary>
        /// 高危风险行为确认。返回 true 仅当用户连续 <paramref name="rounds"/> 次都点「是」。
        /// </summary>
        /// <param name="actionLabel">操作名称（如“安排开机修复磁盘”）。</param>
        /// <param name="command">要执行的命令原文。</param>
        /// <param name="consequence">具体影响说明（如“会删除……必须重启……”），可为 null。</param>
        /// <param name="rounds">确认轮数，默认 5。</param>
        public static bool ConfirmHighRisk(string actionLabel, string command, string? consequence = null, int rounds = 5)
        {
            if (rounds < 1) rounds = 1;

            for (int i = 1; i <= rounds; i++)
            {
                var sb = new StringBuilder();
                sb.Append(UiLanguage.L("⚠ 高危风险行为警告（", "⚠ HIGH-RISK WARNING ("));
                sb.Append(i).Append('/').Append(rounds).Append(UiLanguage.L("）", ")"));
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(UiLanguage.L("操作：", "Action: ")).AppendLine(actionLabel);
                sb.Append(UiLanguage.L("命令：", "Command: ")).AppendLine(command);
                if (!string.IsNullOrWhiteSpace(consequence))
                {
                    sb.Append(UiLanguage.L("影响：", "Impact: ")).AppendLine(consequence);
                }
                sb.AppendLine();
                sb.Append(UiLanguage.L(
                    "此操作可能导致数据丢失或系统异常，且难以撤销。请确认你已经备份了重要数据。",
                    "This action may cause data loss or system issues and is hard to undo. Make sure you have backed up important data."));
                sb.AppendLine();

                if (i == rounds)
                {
                    sb.Append(UiLanguage.L(
                        "这是最后一次确认。你真的了解风险并要继续吗？",
                        "This is the final confirmation. Do you fully understand the risk and still want to continue?"));
                }
                else
                {
                    sb.Append(UiLanguage.L(
                        "请再次确认你了解风险。点击「否」可随时中止。",
                        "Please confirm again that you understand the risk. Click No to abort at any time."));
                }

                var r = MessageBox.Show(
                    sb.ToString(),
                    UiLanguage.L("⚠ 高危风险行为确认（", "⚠ High-risk confirmation (") + i + "/" + rounds
                        + UiLanguage.L("）", ")"),
                    MessageBoxButton.YesNo,
                    i == rounds ? MessageBoxImage.Warning : MessageBoxImage.Question,
                    MessageBoxResult.No);

                if (r != MessageBoxResult.Yes) return false;
            }
            return true;
        }

        /// <summary>
        /// 普通风险操作二次确认（比高危低一档，但仍是删除/覆盖类操作）。
        /// 返回 true 仅当用户连续点 2 次「是」。
        /// </summary>
        public static bool ConfirmTwice(string actionLabel, string detail)
        {
            if (!ConfirmHighRisk(actionLabel, detail, rounds: 2)) return false;
            return true;
        }
    }
}
