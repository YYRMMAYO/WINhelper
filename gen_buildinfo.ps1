# 由 MSBuild 在编译前调用，自动生成 BuildInfo.g.cs
param([string]$Timestamp, [string]$OutputPath)

$content = @"
// 此文件由 MSBuild 在编译时自动生成，请勿手动编辑
// 构建时间戳用于人工更新时区分不同构建版本
namespace WINHELP
{
    internal static class BuildInfo
    {
        /// <summary>程序编译的时间戳（本地时间），每次编译自动更新</summary>
        public const string BuildTimestamp = "$Timestamp";
    }
}
"@

$content | Out-File -FilePath $OutputPath -Encoding utf8 -NoNewline
Write-Host "BuildInfo.g.cs 已生成: $Timestamp"
