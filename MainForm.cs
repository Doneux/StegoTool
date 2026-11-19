using System.ComponentModel;
using System.Drawing.Imaging;
using System.Text;

namespace StegoTool;

// The UI is built in code (no .Designer.cs). This attribute makes double-clicking
// the file in Visual Studio open the code instead of an empty designer.
[DesignerCategory("")]
public sealed class MainForm : Form
{
    readonly PictureBox inputBox = MakePictureBox();
    readonly PictureBox outputBox = MakePictureBox();
    readonly TextBox messageBox = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly TextBox decodedBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly Label capacityLabel = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
    readonly CheckBox highlightBox = new() { Text = "Highlight changed pixels", Dock = DockStyle.Bottom, AutoSize = true, Enabled = false };
    readonly Button encodeButton = new() { Text = "Encode  ↓", AutoSize = true, MinimumSize = new Size(110, 32), Enabled = false };
    readonly Button decodeButton = new() { Text = "Decode input", AutoSize = true, MinimumSize = new Size(110, 32), Enabled = false };
    readonly ToolStripMenuItem saveItem;
    readonly ToolStripStatusLabel statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    Bitmap? input, output, diff;
    string? inputName;

    public MainForm()
    {
        Text = "Stego Tool";
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;

        // ---- Menu ----
        var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        var openItem = new ToolStripMenuItem("&Open Image...", null, (_, _) => OpenImage()) { ShortcutKeys = Keys.Control | Keys.O };
        saveItem = new ToolStripMenuItem("&Save Encoded Image As...", null, (_, _) => SaveOutput()) { ShortcutKeys = Keys.Control | Keys.S, Enabled = false };
        var exitItem = new ToolStripMenuItem("E&xit", null, (_, _) => Close());
        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { openItem, saveItem, new ToolStripSeparator(), exitItem });
        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add("&How it works", null, (_, _) => ShowHowItWorks());
        menu.Items.AddRange(new ToolStripItem[] { fileMenu, helpMenu });
        MainMenuStrip = menu;

        // ---- Status bar ----
        var status = new StatusStrip();
        status.Items.Add(statusLabel);
        SetStatus("Open an image with File → Open (Ctrl+O), or drag one onto the window.");

        // ---- Left column: input image over encoded image ----
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        left.Controls.Add(Group("Input image", inputBox), 0, 0);
        var outGroup = Group("Encoded image", outputBox);
        outGroup.Controls.Add(highlightBox);
        outputBox.BringToFront(); // fill-docked control must be in front of the bottom-docked one
        left.Controls.Add(outGroup, 0, 1);

        // ---- Right column: message, buttons, decoded text ----
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var msgGroup = Group("Message to hide", messageBox);
        msgGroup.Controls.Add(capacityLabel);
        messageBox.BringToFront();
        right.Controls.Add(msgGroup, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(4, 6, 4, 6) };
        buttons.Controls.AddRange(new Control[] { encodeButton, decodeButton });
        right.Controls.Add(buttons, 0, 1);
        right.Controls.Add(Group("Decoded message", decodedBox), 0, 2);

        // ---- Root: two equal columns ----
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);

        Controls.Add(root);   // added first so it fills what the menu and status bar leave
        Controls.Add(status);
        Controls.Add(menu);

        // ---- Events ----
        encodeButton.Click += (_, _) => Encode();
        decodeButton.Click += (_, _) => Decode();
        messageBox.TextChanged += (_, _) => UpdateCapacity();
        highlightBox.CheckedChanged += (_, _) => outputBox.Image = highlightBox.Checked ? diff : output;

        // Right-click menu on the encoded image
        var outputMenu = new ContextMenuStrip();
        outputMenu.Items.Add("&Copy Image", null, (_, _) => CopyOutput());
        outputMenu.Items.Add("&Save As...", null, (_, _) => SaveOutput());
        outputMenu.Opening += (_, e) => e.Cancel = output is null; // nothing to copy yet
        outputBox.ContextMenuStrip = outputMenu;

        // Drag and drop an image anywhere on the window (or onto the input preview)
        foreach (Control target in new Control[] { this, inputBox })
        {
            target.AllowDrop = true;
            target.DragEnter += OnDragEnter;
            target.DragDrop += OnDragDrop;
        }

        UpdateCapacity();
    }

    // ================= Actions =================

    void OpenImage()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open image",
            Filter = "Images|*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.tif;*.tiff|All files|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadImage(dlg.FileName);
    }

    void LoadImage(string path)
    {
        Bitmap loaded;
        try
        {
            // Copy into memory so the file isn't left locked (you can overwrite it later).
            using var fs = File.OpenRead(path);
            using var img = Image.FromStream(fs);
            loaded = new Bitmap(img);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open that image:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var old = input;
        input = loaded;
        inputBox.Image = loaded;
        old?.Dispose();

        inputName = Path.GetFileNameWithoutExtension(path);
        ClearOutput();
        decodedBox.Clear();
        encodeButton.Enabled = decodeButton.Enabled = true;
        UpdateCapacity();

        string ext = Path.GetExtension(path).ToLowerInvariant();
        string note = ext is ".jpg" or ".jpeg" ? "  (JPEG is fine as input; the encoded result is always saved as PNG.)" : "";
        SetStatus($"Opened {Path.GetFileName(path)}: {loaded.Width:N0} × {loaded.Height:N0}.{note}");
    }

    void Encode()
    {
        if (input is null) return;

        Bitmap encoded;
        try { encoded = Stego.Encode(input, messageBox.Text); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Message too long", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ClearOutput();
        output = encoded;
        diff = Stego.DiffMap(input, encoded, out int changed);
        outputBox.Image = highlightBox.Checked ? diff : output;
        highlightBox.Enabled = true;
        saveItem.Enabled = true;

        int bytes = Encoding.UTF8.GetByteCount(messageBox.Text);
        double pct = 100.0 * changed / ((double)input.Width * input.Height);
        SetStatus($"Hid {bytes:N0} bytes. {changed:N0} pixels changed ({pct:0.###}%), each by at most 1 per channel. Ctrl+S to save as PNG.");
    }

    void Decode()
    {
        if (input is null) return;
        string? message = Stego.Decode(input);
        if (message is null)
        {
            decodedBox.Clear();
            SetStatus("No hidden message found in the input image.");
            return;
        }
        decodedBox.Text = message;
        SetStatus($"Found a hidden message: {Encoding.UTF8.GetByteCount(message):N0} bytes.");
    }

    void SaveOutput()
    {
        if (output is null) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Save encoded image",
            Filter = "PNG image|*.png",
            DefaultExt = "png",
            FileName = (inputName ?? "image") + "_encoded.png"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            output.Save(dlg.FileName, ImageFormat.Png); // must be lossless, JPEG would wipe out the hidden bits
            SetStatus($"Saved {Path.GetFileName(dlg.FileName)}. Open it and click Decode to check the message survived.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void CopyOutput()
    {
        if (output is null) return;
        try
        {
            // Always copies the real encoded image, even if the highlight view is showing.
            // Two formats: "PNG" (lossless, kept by browsers, Discord, Paint.NET, etc.)
            // and a standard bitmap for older apps like MS Paint.
            var png = new MemoryStream();
            output.Save(png, ImageFormat.Png);
            png.Position = 0;

            var data = new DataObject();
            data.SetData("PNG", false, png);
            data.SetImage(output);
            Clipboard.SetDataObject(data, copy: true); // copy: true keeps it after the app closes

            SetStatus("Encoded image copied. Note: apps that recompress pasted images (or convert them to JPEG) will destroy the message.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't copy to the clipboard:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void ShowHowItWorks() => MessageBox.Show(this,
        "Every pixel has red, green and blue values from 0 to 255. This tool replaces the lowest bit " +
        "of each of those three values with one bit of your message, so each pixel stores 3 bits and no color " +
        "moves by more than 1, which is invisible to the eye.\n\n" +
        "The first 8 hidden bytes are a \"STG1\" marker and the message length, so Decode knows whether a " +
        "message exists and exactly where it ends (no delimiter needed). Text is stored as UTF-8, so emoji and " +
        "accented characters work.\n\n" +
        "Always save as PNG. Lossy formats like JPEG change pixel values and destroy the message.\n\n" +
        "Tip: tick \"Highlight changed pixels\" after encoding to see which pixels were touched.",
        "How it works", MessageBoxButtons.OK, MessageBoxIcon.Information);

    // ================= Helpers =================

    void ClearOutput()
    {
        outputBox.Image = null;
        output?.Dispose(); output = null;
        diff?.Dispose(); diff = null;
        highlightBox.Enabled = false;
        saveItem.Enabled = false;
    }

    void UpdateCapacity()
    {
        int used = Encoding.UTF8.GetByteCount(messageBox.Text);
        if (input is null)
        {
            capacityLabel.Text = $"{used:N0} bytes";
            capacityLabel.ResetForeColor();
            return;
        }
        int capacity = Stego.CapacityBytes(input.Width, input.Height);
        capacityLabel.Text = $"{used:N0} / {capacity:N0} bytes";
        if (used > capacity) capacityLabel.ForeColor = Color.Red;
        else capacityLabel.ResetForeColor();
    }

    void SetStatus(string text) => statusLabel.Text = text;

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) LoadImage(files[0]);
    }

    static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(content);
        return group;
    }

    static PictureBox MakePictureBox() => new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle
    };
}
