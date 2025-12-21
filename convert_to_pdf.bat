@echo off
chcp 65001
echo 正在转换 Markdown 到 PDF...

pandoc -s "xLua-il2cpp优化解析_PPT.md" ^
    --pdf-engine=xelatex ^
    -V mainfont="SimSun" ^
    -V geometry:margin=1in ^
    -V CJKmainfont="SimSun" ^
    --highlight-style=tango ^
    -o "xLua-il2cpp优化解析_PPT.pdf"

if %errorlevel% equ 0 (
    echo 转换成功！PDF 文件已生成：xLua-il2cpp优化解析_PPT.pdf
) else (
    echo 转换失败，请检查是否安装了 XeLaTeX 和 SimSun 字体
    echo.
    echo 如果使用 pdflatex，请尝试：
    echo pandoc -s "xLua-il2cpp优化解析_PPT.md" --pdf-engine=pdflatex -V CJKmainfont="SimSun" -o output.pdf
)

pause

