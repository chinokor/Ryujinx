﻿using Gtk;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.FileSystem.Content;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using Ryujinx.Ui.Widgets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Image = SixLabors.ImageSharp.Image;
using UserId = Ryujinx.HLE.HOS.Services.Account.Acc.UserId;

namespace Ryujinx.Ui.Windows
{
    public partial class UserProfilesManagerWindow : Window
    {
        private const uint MaxProfileNameLength = 0x20;

        private readonly AccountManager    _accountManager;
        private readonly ContentManager    _contentManager;
        private readonly VirtualFileSystem _virtualFileSystem;

        private byte[] _bufferImageProfile;
        private string _tempNewProfileName;

        private Gdk.RGBA _selectedColor;

        public UserProfilesManagerWindow(AccountManager accountManager, ContentManager contentManager, VirtualFileSystem virtualFileSystem) : base($"Ryujinx {Program.Version} - Manage User Profiles")
        {
            Icon = new Gdk.Pixbuf(Assembly.GetExecutingAssembly(), "Ryujinx.Ui.Resources.Logo_Ryujinx.png");

            InitializeComponent();

            _selectedColor.Red   = 0.212;
            _selectedColor.Green = 0.843;
            _selectedColor.Blue  = 0.718;
            _selectedColor.Alpha = 1;

            _accountManager    = accountManager;
            _contentManager    = contentManager;
            _virtualFileSystem = virtualFileSystem;

            CellRendererToggle userSelectedToggle = new CellRendererToggle();
            userSelectedToggle.Toggled += UserSelectedToggle_Toggled;

            _usersTreeView.AppendColumn("Selected",  userSelectedToggle,       "active", 0);
            _usersTreeView.AppendColumn("User Icon", new CellRendererPixbuf(), "pixbuf", 1);
            _usersTreeView.AppendColumn("User Info", new CellRendererText(),   "text",   2, "background-rgba", 3);

            _tableStore.SetSortColumnId(0, SortType.Descending);
            
            RefreshList();
        }

        public void RefreshList()
        {
            _tableStore.Clear();

            foreach (UserProfile userProfile in _accountManager.GetAllUsers())
            {
                _tableStore.AppendValues(userProfile.AccountState == AccountState.Open, new Gdk.Pixbuf(userProfile.Image, 96, 96), $"{userProfile.Name}\n{userProfile.UserId}", Gdk.RGBA.Zero);

                if (userProfile.AccountState == AccountState.Open)
                {
                    _selectedUserImage.Pixbuf   = new Gdk.Pixbuf(userProfile.Image, 96, 96);
                    _selectedUserIdLabel.Text   = userProfile.UserId.ToString();
                    _selectedUserNameEntry.Text = userProfile.Name;

                    _deleteButton.Sensitive = userProfile.UserId != AccountManager.DefaultUserId;

                    _usersTreeView.Model.GetIterFirst(out TreeIter firstIter);
                    _tableStore.SetValue(firstIter, 3, _selectedColor);
                }
            }
        }

        //
        // Events
        //

        private void UsersTreeView_Activated(object o, RowActivatedArgs args)
        {
            SelectUserTreeView();
        }

        private void UserSelectedToggle_Toggled(object o, ToggledArgs args)
        {
            SelectUserTreeView();
        }

        private void SelectUserTreeView()
        {
            // Get selected item informations.
            _usersTreeView.Selection.GetSelected(out TreeIter selectedIter);

            Gdk.Pixbuf userPicture = (Gdk.Pixbuf)_tableStore.GetValue(selectedIter, 1);

            string userName = _tableStore.GetValue(selectedIter, 2).ToString().Split("\n")[0];
            string userId   = _tableStore.GetValue(selectedIter, 2).ToString().Split("\n")[1];

            // Unselect the first user.
            _usersTreeView.Model.GetIterFirst(out TreeIter firstIter);
            _tableStore.SetValue(firstIter, 0, false);
            _tableStore.SetValue(firstIter, 3, Gdk.RGBA.Zero);

            // Set new informations.
            _tableStore.SetValue(selectedIter, 0, true);

            _selectedUserImage.Pixbuf        = userPicture;
            _selectedUserNameEntry.Text      = userName;
            _selectedUserIdLabel.Text        = userId;
            _saveProfileNameButton.Sensitive = false;

            // Open the selected one.
            _accountManager.OpenUser(new UserId(userId));

            _deleteButton.Sensitive = userId != AccountManager.DefaultUserId.ToString();

            _tableStore.SetValue(selectedIter, 3, _selectedColor);
        }

        private void SelectedUserNameEntry_KeyReleaseEvent(object o, KeyReleaseEventArgs args)
        {
            if (_saveProfileNameButton.Sensitive == false)
            {
                _saveProfileNameButton.Sensitive = true;
            }
        }

        private void AddButton_Pressed(object sender, EventArgs e)
        {
            _tempNewProfileName = GtkDialog.CreateInputDialog(this, "Choose the Profile Name", "Please Enter a Profile Name", MaxProfileNameLength);

            if (_tempNewProfileName != "")
            {
                SelectProfileImage(true);

                if (_bufferImageProfile != null)
                {
                    AddUser();
                }
            }
        }

        private void DeleteButton_Pressed(object sender, EventArgs e)
        {
            if (GtkDialog.CreateChoiceDialog("Delete User Profile", "Are you sure you want to delete the profile ?", "Deleting this profile will also delete all associated save data."))
            {
                _accountManager.DeleteUser(GetSelectedUserId());

                RefreshList();
            }
        }

        private void EditProfileNameButton_Pressed(object sender, EventArgs e)
        {
            _saveProfileNameButton.Sensitive = false;

            _accountManager.SetUserName(GetSelectedUserId(), _selectedUserNameEntry.Text);

            RefreshList();
        }

        private void ProcessProfileImage(byte[] buffer)
        {
            using (Image image = Image.Load(buffer))
            {
                image.Mutate(x => x.Resize(256, 256));

                using (MemoryStream streamJpg = new MemoryStream())
                {
                    image.SaveAsJpeg(streamJpg);

                    _bufferImageProfile = streamJpg.ToArray();
                }
            }
        }

        private void ProfileImageFileChooser()
        {
            FileChooserDialog fileChooser = new FileChooserDialog("Select Custom Profile Image", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "Add", ResponseType.Accept)
            {
                SelectMultiple = false,
                Filter         = new FileFilter()
            };

            fileChooser.SetPosition(WindowPosition.Center);
            fileChooser.Filter.AddPattern("*.jpg");
            fileChooser.Filter.AddPattern("*.jpeg");
            fileChooser.Filter.AddPattern("*.png");
            fileChooser.Filter.AddPattern("*.bmp");

            if (fileChooser.Run() == (int)ResponseType.Accept)
            {
                ProcessProfileImage(File.ReadAllBytes(fileChooser.Filename));
            }

            fileChooser.Dispose();
        }

        private void SelectProfileImage(bool newUser = false)
        {
            SystemVersion firmwareVersion = _contentManager.GetCurrentFirmwareVersion();

            if (firmwareVersion == null)
            {
                ProfileImageFileChooser();
            }
            else
            {
                Dictionary<int, string> buttons = new Dictionary<int, string>()
                {
                    { 0, "Upload Image File"      },
                    { 1, "Select Firmware Avatar" }
                };

                ResponseType responseDialog = GtkDialog.CreateCustomDialog("Profile Image Selection",
                                                                           "Choose a Profile Image",
                                                                           "You may upload a custom profile image, or select an avatar from the system firmware.", 
                                                                           buttons, MessageType.Question);

                if (responseDialog == 0)
                {
                    ProfileImageFileChooser();
                }
                else if (responseDialog == (ResponseType)1)
                {
                    AvatarWindow avatarWindow = new AvatarWindow(_contentManager, _virtualFileSystem)
                    {
                        NewUser = newUser
                    };

                    avatarWindow.DeleteEvent += AvatarWindow_DeleteEvent;

                    avatarWindow.SetSizeRequest((int)(avatarWindow.DefaultWidth * Program.WindowScaleFactor), (int)(avatarWindow.DefaultHeight * Program.WindowScaleFactor));
                    avatarWindow.Show();
                }
            }
        }

        private void ChangeProfileImageButton_Pressed(object sender, EventArgs e)
        {
            SelectProfileImage();

            if (_bufferImageProfile != null)
            {
                SetUserImage();
            }
        }

        private void AvatarWindow_DeleteEvent(object sender, DeleteEventArgs args)
        {
            _bufferImageProfile = ((AvatarWindow)sender).SelectedProfileImage;

            if (_bufferImageProfile != null)
            {
                if (((AvatarWindow)sender).NewUser)
                {
                    AddUser();
                }
                else
                {
                    SetUserImage();
                }
            }
        }

        private void AddUser()
        {
            _accountManager.AddUser(_tempNewProfileName, _bufferImageProfile);

            _bufferImageProfile = null;
            _tempNewProfileName = "";

            RefreshList();
        }

        private void SetUserImage()
        {
            _accountManager.SetUserImage(GetSelectedUserId(), _bufferImageProfile);

            _bufferImageProfile = null;

            RefreshList();
        }

        private UserId GetSelectedUserId()
        {
            if (_usersTreeView.Model.GetIterFirst(out TreeIter iter))
            {
                do
                {
                    if ((bool)_tableStore.GetValue(iter, 0))
                    {
                        break;
                    }
                }
                while (_usersTreeView.Model.IterNext(ref iter));
            }

            return new UserId(_tableStore.GetValue(iter, 2).ToString().Split("\n")[1]);
        }

        private void CloseButton_Pressed(object sender, EventArgs e)
        {
            Close();
        }
    }
}