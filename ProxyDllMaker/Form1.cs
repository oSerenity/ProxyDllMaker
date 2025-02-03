using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProxyDllMaker
{
    public partial class Form1 : Form
    {
        private List<Helper.ExportInfo> _exportList;
        private PeHeaderReader _header;
        private string _lastFileName;
        private OptionForm _optionForm;
        private StringBuilder _sb = new StringBuilder();
        private UnDecorator _unDecorator;

        public Form1()
        {
            InitializeComponent();
            LoadSettings();
            listBox1.ContextMenuStrip = contextMenuStrip1;
        }

        private void Generate(object sender, EventArgs e)
        {
            GenerateDefinitions();
            string temp = Properties.Resources.template_cpp;
            int count = 0;
            if (_exportList != null)
                foreach (var export in _exportList)
                if (export.WayOfExport == 1 || export.WayOfExport == 2)
                    count++;

            temp = count != 0 ? temp.Replace("##ph1##", $"FARPROC p[{count}] = {{0}};") : temp.Replace("##ph1##", "");
            temp = temp.Replace("##ph2##", _lastFileName + Options.suffix);

            _sb = new StringBuilder();
            count = 0;
            if (_exportList != null)
                foreach (var export in _exportList)
                if (export.WayOfExport == 1 || export.WayOfExport == 2)
                    _sb.AppendFormat("\t\tp[{0}] = GetProcAddress(hL,\"{1}\");\n", count++, export.Name);

            temp = temp.Replace("##ph3##", _sb.ToString());
            _sb.Clear();
            count = 0;
            if (_exportList != null)
                foreach (var export in _exportList)
            {
                if (!export.isEmpty)
                {
                    switch (export.WayOfExport)
                    {
                        case 1:
                            _sb.AppendLine(MakeAsmJump(export, count++));
                            break;
                        case 2:
                            _sb.AppendLine(MakeCall(export, count++));
                            break;
                        case 3:
                            _sb.AppendLine(MakeLink(export));
                            break;
                    }
                }
            }

            temp = temp.Replace("##ph4##", _sb.ToString());
            rtb2.Text = temp;
        }
        private void listBox1_Click(object sender, EventArgs e)
        {
            RefreshPreview();
        }

        private void ListBoxDoubleClick(object sender, EventArgs e)
        {
            int n = listBox1.SelectedIndex;
            if (n == -1) return;

            FunctionDialog d = new FunctionDialog();
            d.info = _exportList[n];
            d.header = _header;

            if (d.ShowDialog() == DialogResult.OK)
            {
                _exportList[n] = d.info;
                RefreshExportList();
            }
        }
        private void ListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshPreview();
        }
        private void LoadDefinition(object sender, EventArgs e)
        {
            OpenFileDialog d = new OpenFileDialog { Filter = "*.c;*.h;*.txt|*.c;*.h;*.txt" };
            if (d.ShowDialog() != DialogResult.OK) return;

            string[] input = File.ReadAllLines(d.FileName);
            _sb = new StringBuilder();

            for (int i = 0; i < _exportList.Count; i++)
            {
                if (_exportList[i].WayOfExport == 2)
                {
                    bool found = false;
                    foreach (string line in input)
                    {
                        if (line.Contains(_exportList[i].Name + "("))
                        {
                            var info = _exportList[i];
                            info.Definition = line.Trim();
                            _exportList[i] = info;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        _sb.AppendLine(_exportList[i].Name);
                        var ex = _exportList[i];
                        ex.WayOfExport = 0;
                        _exportList[i] = ex;
                    }
                }
            }

            RefreshExportList();

            if (_sb.Length != 0)
            {
                MessageBox.Show("Error: no definition(s) found for:\n" + _sb.ToString() + "\n please use \"asm jmp\" or \"link\" as method for those exports");
            }
        }

        private void LoadExports(string fileName)
        {
            try
            {
                _exportList = Helper.GetExports(fileName, Options.symMethod);
                RefreshExportList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error:\n" + ex.Message);
            }
        }

        private void LoadSettings()
        {
            if (File.Exists("settings.txt"))
                Options.LoadFromFile("settings.txt");
        }
        private string MakeAsmJump(Helper.ExportInfo export, int index)
        {
            return $"extern \"C\" __declspec(naked) void {Options.prefix}{export.Name}() {{\n" +
                   "    __asm {\n" +
                   $"        jmp p[{index} * 4];\n" +
                   "    }\n" +
                   "}";
        }

        private string MakeCall(Helper.ExportInfo export, int index)
        {
            return $"extern \"C\" void {Options.prefix}{export.Name}() {{\n" +
                   $"    typedef void (*func)();\n" +
                   $"    func f = (func)p[{index}];\n" +
                   "    f();\n" +
                   "}";
        }

        private string MakeLink(Helper.ExportInfo export)
        {
            return $"#pragma comment(linker, \"/export:{export.Name}={_lastFileName}{Options.suffix}.{export.Name}\")";
        }

        private void OpenDLLFile(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "*.dll|*.dll" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                _lastFileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                _header = new PeHeaderReader(dialog.FileName);
                SetStatus(_header.Is32BitHeader);
                rtb1.Text = Helper.DumpObject(_header);

                LoadExports(dialog.FileName);
            }
        }

        private void OpenOptions(object sender, EventArgs e)
        {
            _optionForm?.Close();
            _optionForm = new OptionForm();
            _optionForm.Show();
        }
        private void OpenUnDecorator(object sender, EventArgs e)
        {
            _unDecorator?.Close();
            _unDecorator = new UnDecorator();
            _unDecorator.Show();
        }

        private void RefreshExportList()
        {
            listBox1.Items.Clear();
            listBox1.Visible = false;

            if (_exportList != null)
            {
                foreach (var export in _exportList)
                    listBox1.Items.Add(Helper.ExportInfoToString(export));
            }

            listBox1.Visible = true;
        }

        private void RefreshPreview()
        {
            int n = listBox1.SelectedIndex;
            if (n == -1) return;

            var info = _exportList[n];
            _sb = new StringBuilder();
            _sb.AppendFormat("Index\t\t: {0}\n", info.Index);
            _sb.Append("Export\t:");
            switch (info.WayOfExport)
            {
                case 0:
                    _sb.Append(" not exported");
                    break;
                case 1:
                    _sb.Append(" with asm jmp");
                    break;
                case 2:
                    _sb.Append(" with call");
                    break;
                case 3:
                    _sb.Append(" with link");
                    break;
            }
            _sb.AppendLine();
            _sb.AppendFormat("Name\t\t: {0}\n", info.Name);
            _sb.AppendFormat("Definition\t: {0}", info.Definition);
            rtb4.Text = _sb.ToString();
        }

        private void SaveCFile(object sender, EventArgs e) => SaveFile("c", rtb2.Text);
        private void SaveDEFFile(object sender, EventArgs e) => SaveFile("def", rtb3.Text);

        private void SaveFile(string extension, string content)
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = $"{_lastFileName}.{extension}|{_lastFileName}.{extension}", FileName = $"{_lastFileName}.{extension}" })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(dialog.FileName, content);
                    MessageBox.Show("Done.");
                }
            }
        }

        private void SetStatus(bool is32Bit)
        {
            status.Text = is32Bit ? "32 BIT" : "64 BIT";
            statusStrip1.BackColor = is32Bit ? Color.LightGreen : Color.LightBlue;
            withasmJumpsToolStripMenuItem.Enabled = is32Bit;
        }

        private void UpdateExportMethod(int method)
        {
            if (listBox1.SelectedIndices.Count == 0)
            {
                for (int i = 0; i < listBox1.Items.Count; i++)
                    listBox1.SetSelected(i, true);
            }

            foreach (int index in listBox1.SelectedIndices)
            {
                var export = _exportList[index];
                export.WayOfExport = method;
                _exportList[index] = export;
            }

            RefreshExportList();
        }
        private void GenerateDefinitions()
        {
            _sb.Clear();
            _sb.AppendLine("EXPORTS");
            int count = 1;

            if (_exportList != null)
            {
                foreach (var export in _exportList)
                {
                    _sb.AppendFormat("{0} = {1}{0} @{2}\n", export.Name, Options.prefix, count++);
                }
            }

            rtb3.Text = _sb.ToString();
        }
        private void WithAsmJumps(object sender, EventArgs e) => UpdateExportMethod(1);
        private void WithCalls(object sender, EventArgs e) => UpdateExportMethod(2);
        private void WithLinks(object sender, EventArgs e) => UpdateExportMethod(3);

        private void GenJustDef(object sender, EventArgs e)
        {
            if (_exportList != null)
            {
                GenerateDefinitions();

                if (MessageBox.Show("Would You like to Save To Local Storage?", "Save Definition File?", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    SaveFile("def", rtb3.Text);
                }
            }
        }

        private void genarateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(_exportList != null)
            Generate(sender, e);
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Convert listBox1 items to a single string
            string input = string.Join(",", listBox1.Items.Cast<object>());

            // Extract all matches inside quotes
            var matches = Regex.Matches(input, "\"(.*?)\"")
                               .Cast<Match>()
                               .Select(m => m.Groups[1].Value);
            if(input.Length != 0)
            // Copy extracted values to clipboard with newline separation
            Clipboard.SetText(string.Join("\r\n", matches));
        }
    }
}
