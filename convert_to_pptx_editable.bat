@echo off
chcp 65001
echo ========================================
echo  Markdown 转可编辑 PPTX 工具
echo ========================================
echo.

REM 检查 Pandoc 是否安装
where pandoc >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 未检测到 Pandoc！
    echo.
    echo 请先安装 Pandoc：
    echo   1. 使用 Chocolatey: choco install pandoc
    echo   2. 或从官网下载: https://pandoc.org/installing.html
    echo.
    pause
    exit /b 1
)

echo [信息] 检测到 Pandoc，开始转换...
echo.

set INPUT_FILE=xLua-il2cpp优化解析_Marp.md
set OUTPUT_FILE=xLua-il2cpp优化解析_Marp_可编辑.pptx

REM 检查输入文件是否存在
if not exist "%INPUT_FILE%" (
    echo [错误] 找不到输入文件: %INPUT_FILE%
    echo.
    pause
    exit /b 1
)

echo 输入文件: %INPUT_FILE%
echo 输出文件: %OUTPUT_FILE%
echo.

REM 执行转换
pandoc "%INPUT_FILE%" ^
    -o "%OUTPUT_FILE%" ^
    --from markdown ^
    --to pptx ^
    --slide-level 2 ^
    --standalone

if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo  [成功] 转换完成！
    echo ========================================
    echo.
    echo 输出文件: %OUTPUT_FILE%
    echo.
    echo 提示：
    echo   - 生成的 PPTX 文件是可编辑的
    echo   - 可以在 PowerPoint 中打开并调整格式
    echo   - 如需更好的格式，可以使用 --reference-doc 指定模板
    echo.
) else (
    echo.
    echo ========================================
    echo  [失败] 转换出错
    echo ========================================
    echo.
    echo 可能的原因：
    echo   1. 输入文件格式有问题
    echo   2. Pandoc 版本过旧
    echo   3. 文件路径包含特殊字符
    echo.
)

pause

