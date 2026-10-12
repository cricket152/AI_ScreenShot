using ScreenShot.Core;

internal static class MarkdownTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        string Plain(IReadOnlyList<MarkdownInline> inlines)=>string.Concat(inlines.Select(part=>part.Text));

        const string displayMath="前文\n\n\\[\n1 \\xrightarrow{w} 2 \\quad 1 \\xrightarrow{e} 5\n\\]\n\n后文";
        var mathDocument=MarkdownDocument.Parse(displayMath);
        Check(mathDocument.Blocks.Count==3&&mathDocument.Blocks[1] is MarkdownMathBlock,"Bracket-delimited equations are parsed before Markdown escapes");
        var mathBlock=(MarkdownMathBlock)mathDocument.Blocks[1];
        Check(mathBlock.LaTeX.Contains(@"\xrightarrow{w}")&&mathBlock.Source=="\\[\n1 \\xrightarrow{w} 2 \\quad 1 \\xrightarrow{e} 5\n\\]","Math commands and original delimiters are lossless");
        foreach(string delimiter in new[]{@"\[", "$$"})
        {
            string close=delimiter=="$$"?"$$":@"\]";
            Check(MarkdownDocument.Parse(delimiter+@"\frac{a_1}{b^2}"+close).Blocks[0] is MarkdownMathBlock {LaTeX:@"\frac{a_1}{b^2}"},"One-line display math preserves nested fraction scripts");
        }
        foreach(string inline in new[]{@"\(\Sigma=\{w,e,b\}\)",@"$\Sigma=\{w,e,b\}$"})
        {
            var parts=MarkdownDocument.ParseInlines("字母表："+inline+"；专业术语 NFA。");
            Check(parts.Single(part=>part.Math!=null).Math==@"\Sigma=\{w,e,b\}","Inline math is opaque to Markdown brace escapes");
            Check(Plain(parts)=="字母表："+inline+"；专业术语 NFA。","Professional terminology and formula source remain intact");
        }
        var boldMath=MarkdownDocument.ParseInlines(@"**概率 $P(A\mid B)$**");
        Check(boldMath.Any(part=>part.Math==@"P(A\mid B)"&&part.Style.HasFlag(MarkdownTextStyle.Bold)),"Math inside emphasized prose remains a math segment");
        var emphasizedProduct=MarkdownDocument.ParseInlines(@"*乘积 $a*b_i$ 与符号* 后文");
        Check(emphasizedProduct.Any(part=>part.Math=="a*b_i"&&part.Style.HasFlag(MarkdownTextStyle.Italic))&&Plain(emphasizedProduct)=="乘积 $a*b_i$ 与符号 后文","Formula operators and scripts do not terminate surrounding Markdown emphasis");
        var codeMath=MarkdownDocument.ParseInlines(@"`\(x_1\)` 与 $x_1$");
        Check(codeMath[0].Math==null&&codeMath[0].Style.HasFlag(MarkdownTextStyle.Code)&&codeMath.Any(part=>part.Math=="x_1"),"Inline code is never interpreted as a formula");
        const string tikz="```tikz\n\\begin{tikzpicture}\n\\draw (0,0) -- (1,1);\n\\end{tikzpicture}\n```";
        Check(MarkdownDocument.Parse(tikz).Blocks[0] is MarkdownCodeBlock {Code:"\\begin{tikzpicture}\n\\draw (0,0) -- (1,1);\n\\end{tikzpicture}"},"Drawing code is preserved verbatim without math parsing or execution");
        var mathTable=(MarkdownTable)MarkdownDocument.Parse("| 公式 | 名称 |\n| --- | --- |\n| $|x|$ | 范数 |\n| \\(\\begin{matrix}1&2\\\\3&4\\end{matrix}\\) | 矩阵 |").Blocks[0];
        Check(mathTable.Rows.Count==3&&mathTable.Rows[1][0].Single().Math=="|x|"&&Plain(mathTable.Rows[1][1])=="范数","Pipes inside formulas do not split table cells");
        Check(mathTable.Rows[2][0].Single().Math!.Contains(@"\begin{matrix}"),"Table formulas retain matrix row delimiters");
        Check(Plain(MarkdownDocument.ParseInlines("价格 $5 和 $10，USD_Amount 不变"))=="价格 $5 和 $10，USD_Amount 不变","Currency and identifiers are not misidentified as formulas");
        Check(Plain(MarkdownDocument.ParseInlines(@"价格 \$5；转义 \\(x\\)"))==@"价格 $5；转义 \(x\)","Explicit escaped dollars and escaped delimiters retain literal meanings");
        foreach(string partial in new[]{@"\(\frac{a_1}",@"$$\frac{a_1}",@"$x_{a_1", "\\[\n\\begin{matrix}\n1&2\\\\\n"})
        {
            var parsed=MarkdownDocument.Parse(partial);
            Check(parsed.Blocks[0] is MarkdownParagraph paragraph&&Plain(paragraph.Inlines)==partial,"Incomplete streamed formulas keep source without consuming escapes or emphasis");
        }
        const string formulaStream="前文\n\n\\[\n\\frac{a_1+b^2}{\\sqrt{x}}\n\\]\n\n后文";
        for(int length=0;length<=formulaStream.Length;length++)Check(MarkdownDocument.Parse(formulaStream[..length]).Blocks!=null,"Every partial mathematical reply parses without throwing");

        Check(MarkdownDocument.Parse(null).Blocks.Count==0,"A null Markdown message is an empty native document");
        Check(MarkdownDocument.Parse(" \n\t\r\n").Blocks.Count==0,"Whitespace-only streaming messages create no blocks");
        var headings=MarkdownDocument.Parse("# 主标题\r\n\r\n## 第二级 ##\r###第三级并非标题\r###### 第六级");
        Check(headings.Blocks[0] is MarkdownHeading {Level:1},"ATX heading levels remain semantic");
        Check(headings.Blocks[1] is MarkdownHeading {Level:2} second&&Plain(second.Inlines)=="第二级","Trailing heading markers are excluded from selectable heading text");
        Check(headings.Blocks[2] is MarkdownParagraph ordinary&&Plain(ordinary.Inlines)=="###第三级并非标题","Heading markers require a separating space");
        Check(headings.Blocks[3] is MarkdownHeading {Level:6},"Six heading levels are accepted");
        Check(MarkdownDocument.Parse("####### text").Blocks[0] is MarkdownParagraph,"Excess heading levels remain readable literal text");

        var styled=MarkdownDocument.ParseInlines("普通 **粗体** *斜体* ~~删除~~ `a*b` __加粗__ _倾斜_ ***两者***");
        Check(Plain(styled)=="普通 粗体 斜体 删除 a*b 加粗 倾斜 两者","Inline markup is removed only after the corresponding delimiter closes");
        foreach(var expected in new[]{("粗体",MarkdownTextStyle.Bold),("斜体",MarkdownTextStyle.Italic),("删除",MarkdownTextStyle.Strike),("a*b",MarkdownTextStyle.Code),("加粗",MarkdownTextStyle.Bold),("倾斜",MarkdownTextStyle.Italic),("两者",MarkdownTextStyle.Bold|MarkdownTextStyle.Italic)})
            Check(styled.Any(part=>part.Text==expected.Item1&&part.Style==expected.Item2),"Native Markdown has the requested style for "+expected.Item1);
        var nested=MarkdownDocument.ParseInlines("**粗体 *内斜体*** 和 *斜体 **内粗体***");
        Check(Plain(nested)=="粗体 内斜体 和 斜体 内粗体","Nested emphasis shares closing delimiter runs without exposing stray markup");
        Check(nested.Any(part=>part.Text=="内斜体"&&part.Style==(MarkdownTextStyle.Bold|MarkdownTextStyle.Italic)),"Italic emphasis nests inside bold text");
        Check(nested.Any(part=>part.Text=="内粗体"&&part.Style==(MarkdownTextStyle.Bold|MarkdownTextStyle.Italic)),"Bold emphasis nests inside italic text");
        Check(Plain(MarkdownDocument.ParseInlines("snake_case_identifier"))=="snake_case_identifier","Intraword underscores remain literal");
        Check(Plain(MarkdownDocument.ParseInlines(@"\*literal\* and \{label\}"))=="*literal* and {label}","Escaped punctuation remains selectable literal text");
        Check(Plain(MarkdownDocument.ParseInlines("`` `literal` ``"))=="`literal`","Variable-width inline code fences preserve literal backticks");
        Check(MarkdownDocument.ParseInlines("**start `**` finish**").Any(part=>part.Text=="**"&&part.Style.HasFlag(MarkdownTextStyle.Code)),"Inline code delimiters do not close enclosing emphasis");
        foreach(string incomplete in new[]{"**unfinished","*unfinished","~~unfinished","`unfinished","[label](https://example.test","![image](https://example.test"})
            Check(Plain(MarkdownDocument.ParseInlines(incomplete))==incomplete,"A partial streamed inline delimiter remains readable without dropping content");

        var codeDocument=MarkdownDocument.Parse("```csharp\nvar value = \"**literal**\";\n| not | a table |\n```\n\n后续");
        Check(codeDocument.Blocks[0] is MarkdownCodeBlock {Language:"csharp",IsClosed:true} code&&code.Code=="var value = \"**literal**\";\n| not | a table |","Fenced code is literal and keeps line boundaries");
        Check(codeDocument.Blocks[1] is MarkdownParagraph,"A closing fence resumes ordinary messages");
        Check(MarkdownDocument.Parse("~~~text\nfirst\nsecond").Blocks[0] is MarkdownCodeBlock {IsClosed:false,Code:"first\nsecond"},"An unfinished streamed code block remains visible");
        Check(MarkdownDocument.Parse("````\n```\n````").Blocks[0] is MarkdownCodeBlock {IsClosed:true,Code:"```"},"A shorter fence remains content inside a longer code fence");

        var quote=(MarkdownQuote)MarkdownDocument.Parse("> 引用 **内容**\n>\n> > 内层引用").Blocks[0];
        Check(quote.Blocks.Count==2&&quote.Blocks[0] is MarkdownParagraph&&quote.Blocks[1] is MarkdownQuote,"Quotes preserve paragraph breaks and nested structure");
        var list=(MarkdownList)MarkdownDocument.Parse("1. **第一条**\n  - 内层条目\n    延续内容\n2) 第二条").Blocks[0];
        Check(list.Items.Count==3&&list.Items[0].Number==1&&list.Items[1].Number==null&&list.Items[1].Depth==1&&list.Items[2].Depth==0,"Ordered and nested unordered list markers retain their hierarchy");
        Check(Plain(list.Items[1].Inlines)=="内层条目\n延续内容","Indented list continuations are kept in the same item");
        Check(MarkdownDocument.Parse("前文\n\n---\n\n后文").Blocks[1] is MarkdownRule,"A thematic divider has its own native block");

        var table=(MarkdownTable)MarkdownDocument.Parse("| 名称 | 值 | 方向 |\n| :--- | :---: | ---: |\n| `a|b` | one\\|two | 3 |\n| 缺列 |\n\n结束").Blocks[0];
        Check(table.Alignments.SequenceEqual(new[]{MarkdownTableAlignment.Left,MarkdownTableAlignment.Center,MarkdownTableAlignment.Right}),"Table alignment is parsed for native cells");
        Check(table.Rows.Count==3&&table.Rows.All(row=>row.Count==3),"Short table rows are padded to the header column count");
        Check(Plain(table.Rows[1][0])=="a|b"&&Plain(table.Rows[1][1])=="one|two","Pipes inside code or escaped pipes do not split table cells");
        Check(Plain(table.Rows[2][1])==""&&Plain(table.Rows[2][2])=="","Missing table cells are empty selectable cells");
        Check(MarkdownDocument.Parse("a | b\n-- | ---").Blocks[0] is MarkdownParagraph,"Malformed table separators remain ordinary paragraph content");

        var link=MarkdownDocument.ParseInlines("[**文档**](https://example.test/docs(a)) 和 <http://example.test/path>");
        Check(link.Any(part=>part.Text=="文档"&&part.Style==MarkdownTextStyle.Bold&&part.Link?.AbsoluteUri=="https://example.test/docs(a)"),"HTTP(S) link labels can contain formatting and balanced path parentheses");
        Check(link.Any(part=>part.Text=="http://example.test/path"&&part.Link!=null),"Safe autolinks are rendered as native clickable links");
        foreach(string unsafeLink in new[]{"javascript:alert(1)","file:///C:/private.txt","data:text/html,test","codex://threads/private","https://user:password@example.test/","https://example.test/\nhidden"})
        {
            string markup="[label]("+unsafeLink+")";var rejected=MarkdownDocument.ParseInlines(markup);
            Check(rejected.All(part=>part.Link==null)&&Plain(rejected)==markup,"Unsafe or credential-bearing link destinations remain literal");
        }
        const string image="![remote image](https://example.test/image.png)";
        Check(Plain(MarkdownDocument.ParseInlines(image))==image&&MarkdownDocument.ParseInlines(image).All(part=>part.Link==null),"Markdown images are literal and cannot request remote content");
        const string html="<img src='https://example.test/image.png'><script>alert(1)</script>";
        Check(Plain(((MarkdownParagraph)MarkdownDocument.Parse(html).Blocks[0]).Inlines)==html,"Raw HTML is displayed as literal text and never interpreted");

        const string streamed="# 标题\n\n**回复** 与 [网站](https://example.test)\n\n```text\nstreamed code\n```\n\n| a | b |\n| --- | --- |\n| 1 | 2 |";
        for(int length=0;length<=streamed.Length;length++)
        {
            var partial=MarkdownDocument.Parse(streamed[..length]);
            Check(partial.Blocks!=null,"Every partial provider delta can produce a native document");
        }
        string deep=string.Concat(Enumerable.Repeat("> ",80))+"deep";
        Check(MarkdownDocument.Parse(deep).Blocks.Count==1,"Deep quote input has a bounded parser recursion depth");
        Check(Plain(((MarkdownParagraph)MarkdownDocument.Parse(new string('a',65536)).Blocks[0]).Inlines).Length==65536,"Large reply text remains intact without arbitrary truncation");
        string longIncomplete="prefix "+new string('*',65536)+string.Concat(Enumerable.Repeat(" [unfinished",4096));
        Check(Plain(MarkdownDocument.ParseInlines(longIncomplete))==longIncomplete,"Large unfinished delimiter runs remain readable without repeated scans or arbitrary truncation");
        return checks;
    }
}
