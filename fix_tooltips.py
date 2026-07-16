import re
with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

replacements = {
    'ToolTip="VAA dAAA sAA?~: Caliper1 &gt; 10 &amp;&amp; \nOrigin.Pass&#x0a;VAA dAAA chAAA_: QR1.Text == &quot;ABC123&quot; || QR1.Text == \n&quot;LOT-01&quot;&#x0a;BiAAAn trong Condition lAAAy theo A,?~AAng tAAn tool: Origin, Caliper1, QR1, \nEP.MyEdge, SC.MySurface, ...&#x0a;So sAAnh chAAA_ hAA?" trAAA == vAA !=; so sAAnh sAA?~ hAA?" \ntrAAA &gt;, &lt;, &gt;=, &lt;=, ==, !="': 'ToolTip="Ví dụ số: Caliper1 &gt; 10 &amp;&amp; Origin.Pass&#x0a;Ví dụ chữ: QR1.Text == &quot;ABC123&quot; || QR1.Text == &quot;LOT-01&quot;&#x0a;Biến trong Condition lấy theo đúng tên tool: Origin, Caliper1, QR1, EP.MyEdge, SC.MySurface, ...&#x0a;So sánh chữ hỗ trợ == và !=; so sánh số hỗ trợ &gt;, &lt;, &gt;=, &lt;=, ==, !="',
    '<TextBlock Text="VAA dAAA: Caliper1 &gt; 10 || QR1.Text == &quot;ABC123&quot; | \nbiAAAn theo tAAn tool: Origin, Caliper1, QR1, EP.*, SC.*"': '<TextBlock Text="Ví dụ: Caliper1 &gt; 10 || QR1.Text == &quot;ABC123&quot; | biến theo tên tool: Origin, Caliper1, QR1, EP.*, SC.*"',
    'ToolTip="DAA1ng placeholder A,?~AA\' ghAAcp kAAAt quAAA tAAA \ntool khAAc vAAo text. HAA?" trAAA cAAA {ToolName.Value} vAA ${ToolName.Value}. VAA dAAA: KAAAt \nquAAA Caliper: {Caliper1.Value:0.000} mm | QR: {QR1.Text} | Origin pass: {Origin.Pass}"': 'ToolTip="Dạng placeholder để ghép kết quả từ tool khác vào text. Hỗ trợ cả {ToolName.Value} và ${ToolName.Value}. Ví dụ: Kết quả Caliper: {Caliper1.Value:0.000} mm | QR: {QR1.Text} | Origin pass: {Origin.Pass}"',
    '<TextBlock Text="VAA dAAA: KAAAt quAAA Caliper: {Caliper1.Value:0.000} mm \n| QR: {QR1.Text} | Origin pass: {Origin.Pass}"': '<TextBlock Text="Ví dụ: Kết quả Caliper: {Caliper1.Value:0.000} mm | QR: {QR1.Text} | Origin pass: {Origin.Pass}"',
    'ToolTip="VAA dAAA: text.Contains(&quot;OK&quot;) || \ntext.Contains(&quot;FAIL&quot;)&#x0a;Trong Text rule, biAAAn thA+AAAA?ng lAA text vAA cAA3 thAA\' ghAAcp \nnhiAAA?u A,?~iAAA?u kiAA?n bAAAng &amp;&amp; / ||.&#x0a;NAAAu muAA?~n A,?~AA?i mAAu theo \nkAAAt quAAA tool khAAc, hAAy A,?~A+Aa kAAAt quAAA A,?~AA3 vAAo placeholder cAAA a Text \nrAA?oi dAA1ng expression AA, A,?~AAy."': 'ToolTip="Ví dụ: text.Contains(&quot;OK&quot;) || text.Contains(&quot;FAIL&quot;)&#x0a;Trong Text rule, biến thường là text và có thể ghép nhiều điều kiện bằng &amp;&amp; / ||.&#x0a;Nếu muốn đổi màu theo kết quả tool khác, hãy đưa kết quả đó vào placeholder của Text rồi dùng expression ở đây."',
    '<TextBlock Text="VAA dAAA: text.Contains(&quot;OK&quot;) || \ntext.Contains(&quot;FAIL&quot;)"': '<TextBlock Text="Ví dụ: text.Contains(&quot;OK&quot;) || text.Contains(&quot;FAIL&quot;)"'
}

# The strings in powershell output had newlines in the middle of them. To be safe, let's use regex to replace.
pattern1 = re.compile(r'ToolTip="VAA dAAA sAA\?~: Caliper1 &gt;.*?=="', re.DOTALL)
text = pattern1.sub('ToolTip="Ví dụ số: Caliper1 &gt; 10 &amp;&amp; Origin.Pass&#x0a;Ví dụ chữ: QR1.Text == &quot;ABC123&quot; || QR1.Text == &quot;LOT-01&quot;&#x0a;Biến trong Condition lấy theo đúng tên tool: Origin, Caliper1, QR1, EP.MyEdge, SC.MySurface, ...&#x0a;So sánh chữ hỗ trợ == và !=; so sánh số hỗ trợ &gt;, &lt;, &gt;=, &lt;=, ==, !="', text)

pattern2 = re.compile(r'<TextBlock Text="VAA dAAA: Caliper1 &gt; 10.*?SC\.\*"', re.DOTALL)
text = pattern2.sub('<TextBlock Text="Ví dụ: Caliper1 &gt; 10 || QR1.Text == &quot;ABC123&quot; | biến theo tên tool: Origin, Caliper1, QR1, EP.*, SC.*"', text)

pattern3 = re.compile(r'ToolTip="DAA1ng placeholder.*?\{Origin\.Pass\}"', re.DOTALL)
text = pattern3.sub('ToolTip="Dạng placeholder để ghép kết quả từ tool khác vào text. Hỗ trợ cả {ToolName.Value} và ${ToolName.Value}. Ví dụ: Kết quả Caliper: {Caliper1.Value:0.000} mm | QR: {QR1.Text} | Origin pass: {Origin.Pass}"', text)

pattern4 = re.compile(r'<TextBlock Text="VAA dAAA: KAAAt quAAA Caliper:.*?\{Origin\.Pass\}"', re.DOTALL)
text = pattern4.sub('<TextBlock Text="Ví dụ: Kết quả Caliper: {Caliper1.Value:0.000} mm | QR: {QR1.Text} | Origin pass: {Origin.Pass}"', text)

pattern5 = re.compile(r'ToolTip="VAA dAAA: text\.Contains.*?A,\?~AAy\."', re.DOTALL)
text = pattern5.sub('ToolTip="Ví dụ: text.Contains(&quot;OK&quot;) || text.Contains(&quot;FAIL&quot;)&#x0a;Trong Text rule, biến thường là text và có thể ghép nhiều điều kiện bằng &amp;&amp; / ||.&#x0a;Nếu muốn đổi màu theo kết quả tool khác, hãy đưa kết quả đó vào placeholder của Text rồi dùng expression ở đây."', text)

pattern6 = re.compile(r'<TextBlock Text="VAA dAAA: text\.Contains.*?FAIL&quot;\)"', re.DOTALL)
text = pattern6.sub('<TextBlock Text="Ví dụ: text.Contains(&quot;OK&quot;) || text.Contains(&quot;FAIL&quot;)"', text)


with open(r'g:\NODEJS\Vision2026\VisionInspectionApp.UI\Views\ToolEditorView.xaml', 'w', encoding='utf-8') as f:
    f.write(text)
print('Tooltips fixed.')
