﻿using Gtk;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.FsSystem.NcaUtils;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.FileSystem.Content;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using Image = SixLabors.ImageSharp.Image;

namespace Ryujinx.Ui.Windows
{
    public class AvatarWindow : Window
    {
        public byte[] SelectedProfileImage;
        public bool   NewUser;

        private Dictionary<string, byte[]> avatarDict = new Dictionary<string, byte[]>();

        private ListStore _listStore;
        private IconView  _iconView;

        public AvatarWindow(ContentManager contentManager, VirtualFileSystem virtualFileSystem) : base($"Ryujinx {Program.Version} - Manage Accounts - Avatar")
        {
            // TODO: Handle the dynamic avatar background. For now we will just uses a white one.

            CanFocus  = false;
            Resizable = false;
            Modal     = true;
            TypeHint  = Gdk.WindowTypeHint.Dialog;

            SetDefaultSize(620, 400);
            SetPosition(WindowPosition.Center);

            VBox vbox = new VBox(false, 0);
            Add(vbox);

            ScrolledWindow scrolledWindow = new ScrolledWindow
            {
                ShadowType = ShadowType.EtchedIn
            };
            scrolledWindow.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);

            HBox hbox = new HBox(false, 0);

            Button chooseButton = new Button()
            {
                Label           = "Choose",
                CanFocus        = true,
                ReceivesDefault = true
            };
            chooseButton.Clicked += ChooseButton_Pressed;

            Button closeButton = new Button()
            {
                Label           = "Close",
                CanFocus        = true
            };
            closeButton.Clicked += CloseButton_Pressed;

            vbox.PackStart(scrolledWindow, true, true, 0);
            hbox.PackStart(chooseButton, true, true, 0);
            hbox.PackStart(closeButton, true, true, 0);
            vbox.PackStart(hbox, false, false, 0);

            _listStore = new ListStore(typeof(string), typeof(Gdk.Pixbuf));
            _listStore.SetSortColumnId(0, SortType.Ascending);

            string contentPath = contentManager.GetInstalledContentPath(0x010000000000080A, StorageId.NandSystem, NcaContentType.Data);
            string avatarPath  = virtualFileSystem.SwitchPathToSystemPath(contentPath);

            if (!string.IsNullOrWhiteSpace(avatarPath))
            {
                using (IStorage ncaFileStream = new LocalStorage(avatarPath, FileAccess.Read, FileMode.Open))
                {
                    Nca         nca   = new Nca(virtualFileSystem.KeySet, ncaFileStream);
                    IFileSystem romfs = nca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.ErrorOnInvalid);

                    foreach (var item in romfs.EnumerateEntries())
                    {
                        // TODO: Parse DatabaseInfo.bin and table.bin files for more accuracy.

                        if (item.Type == DirectoryEntryType.File && item.FullPath.Contains("chara") && item.FullPath.Contains("szs"))
                        {
                            romfs.OpenFile(out IFile file, ("/" + item.FullPath).ToU8Span(), OpenMode.Read).ThrowIfFailure();

                            using (MemoryStream stream    = new MemoryStream())
                            using (MemoryStream streamJpg = new MemoryStream())
                            {
                                file.AsStream().CopyTo(stream);

                                stream.Position = 0;

                                Image avatarImage = Image.LoadPixelData<Rgba32>(DecompressYaz0(stream), 256, 256);

                                avatarImage.Mutate(x => x.BackgroundColor(new Rgba32(255, 255, 255, 255)));
                                avatarImage.SaveAsJpeg(streamJpg);

                                avatarDict.Add(item.FullPath, streamJpg.ToArray());

                                _listStore.AppendValues(item.FullPath, new Gdk.Pixbuf(streamJpg.ToArray(), 96, 96));
                            }
                        }
                    }
                }
            }

            _iconView              = new IconView(_listStore);
            _iconView.ItemWidth    = 64;
            _iconView.ItemPadding  = 10;
            _iconView.PixbufColumn = 1;

            _iconView.SelectionChanged += IconView_SelectionChanged;

            _iconView.SelectPath(new TreePath(new int[] { 0 }));

            scrolledWindow.Add(_iconView);

            _iconView.GrabFocus();

            ShowAll();
        }

        private void CloseButton_Pressed(object sender, EventArgs e)
        {
            SelectedProfileImage = null;

            Close();
        }

        private void IconView_SelectionChanged(object sender, EventArgs e)
        {
            if (_iconView.SelectedItems.Length > 0)
            {
                _listStore.GetIter(out TreeIter iter, _iconView.SelectedItems[0]);

                SelectedProfileImage = avatarDict[(string)_listStore.GetValue(iter, 0)];
            }
        }

        private void ChooseButton_Pressed(object sender, EventArgs e)
        {
            Close();
        }

        public byte[] DecompressYaz0(Stream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.ReadInt32(); // Magic
                
                uint decodedLength = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());

                reader.ReadInt64(); // Padding

                byte[] input = new byte[stream.Length - stream.Position];
                stream.Read(input, 0, input.Length);

                long inputOffset = 0;

                byte[] output       = new byte[decodedLength];
                long   outputOffset = 0;

                ushort mask   = 0;
                byte   header = 0;

                while (outputOffset < decodedLength)
                {
                    if ((mask >>= 1) == 0)
                    {
                        header = input[inputOffset++];
                        mask   = 0x80;
                    }

                    if ((header & mask) > 0)
                    {
                        if (outputOffset == output.Length)
                        {
                            break;
                        }

                        output[outputOffset++] = input[inputOffset++];
                    }
                    else
                    {
                        byte byte1 = input[inputOffset++];
                        byte byte2 = input[inputOffset++];

                        int dist     = ((byte1 & 0xF) << 8) | byte2;
                        int position = (int)outputOffset - (dist + 1);

                        int length = byte1 >> 4;
                        if (length == 0)
                        {
                            length = input[inputOffset++] + 0x12;
                        }
                        else
                        {
                            length += 2;
                        }

                        while (length-- > 0)
                        {
                            output[outputOffset++] = output[position++];
                        }
                    }
                }

                return output;
            }
        }
    }
}