namespace DesktopPet.Setup;

internal sealed class InstallerForm : Form
{
    private readonly Label _statusLabel;
    private readonly Button _installButton;
    private readonly Button _cancelButton;

    internal InstallerForm()
    {
        Text = $"{Program.DisplayName} 安装程序";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 310);
        MinimumSize = new Size(520, 300);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            Location = new Point(28, 25),
            Text = Program.DisplayName
        };
        var version = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(31, 67),
            Text = $"版本 {Program.ProductVersion} · Windows x64 · 当前用户安装"
        };
        var locationCaption = new Label
        {
            AutoSize = true,
            Location = new Point(31, 111),
            Text = "安装位置："
        };
        var location = new TextBox
        {
            Location = new Point(34, 136),
            ReadOnly = true,
            TabStop = false,
            Width = 490,
            Text = InstallerPaths.InstallDirectory
        };
        var note = new Label
        {
            AutoSize = false,
            Location = new Point(31, 174),
            Size = new Size(493, 44),
            Text = $"安装和卸载均不需要管理员权限。卸载时会保留个人设置：\n{InstallerPaths.SettingsDirectory}"
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(31, 226),
            Size = new Size(290, 35),
            ForeColor = SystemColors.GrayText,
            Text = InstallerEngine.IsExistingInstallation ? "检测到已有版本，可更新。" : "准备安装。"
        };
        _installButton = new Button
        {
            Location = new Point(337, 233),
            Size = new Size(90, 32),
            Text = InstallerEngine.IsExistingInstallation ? "更新" : "安装",
            UseVisualStyleBackColor = true
        };
        _cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(434, 233),
            Size = new Size(90, 32),
            Text = "取消",
            UseVisualStyleBackColor = true
        };

        _installButton.Click += InstallButtonOnClick;
        Controls.AddRange(new Control[]
        {
            title, version, locationCaption, location, note,
            _statusLabel, _installButton, _cancelButton
        });
        AcceptButton = _installButton;
        CancelButton = _cancelButton;
    }

    private void InstallButtonOnClick(object? sender, EventArgs eventArgs)
    {
        var isUpdate = InstallerEngine.IsExistingInstallation;
        if (isUpdate)
        {
            var confirmation = MessageBox.Show(
                this,
                "将用此安装包替换现有桌宠程序。个人设置不会被删除。\n\n是否继续更新？",
                Program.DisplayName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }
        }

        if (!DesktopPetProcessGuard.EnsureStopped(this))
        {
            return;
        }

        SetBusy(true, isUpdate ? "正在安全更新…" : "正在安装…");
        try
        {
            InstallerEngine.InstallOrUpdate();
            _statusLabel.Text = isUpdate ? "更新完成。" : "安装完成。";
            MessageBox.Show(
                this,
                $"{Program.DisplayName} 已{(isUpdate ? "更新" : "安装")}完成。\n\n" +
                "可从开始菜单启动桌宠。",
                Program.DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "操作失败，未完成安装。";
            MessageBox.Show(
                this,
                $"安装失败：\n\n{exception.Message}",
                Program.DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        UseWaitCursor = busy;
        _installButton.Enabled = !busy;
        _cancelButton.Enabled = !busy;
        _statusLabel.Text = status;
        Refresh();
    }
}
