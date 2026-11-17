using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TextBlock=Lucent.SmoothText;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ResolutionSwitcher;

namespace Lucent
{
    // The Crosshair page: ready-made styles, a builder for your own, a library of saved looks and shareable codes.
    internal sealed partial class Shell
    {
        readonly List<Action> crosshairViews=new List<Action>(),crosshairSync=new List<Action>();
        bool crosshairSyncing;
        Image crosshairPreview;
        TextBlock crosshairInfo;
        WrapPanel savedCrosshairs;
        readonly List<KeyValuePair<Border,string>> savedTiles=new List<KeyValuePair<Border,string>>();
        CrosshairSettings CrosshairLook {get{return preferences.Crosshair;}}

        FrameworkElement CrosshairPage()
        {
            crosshairViews.Clear();crosshairSync.Clear();savedTiles.Clear();
            // Building the controls reads values back into them; none of that is a change by the user.
            crosshairSyncing=true;
            var page=new StackPanel {Margin=new Thickness(0,16,0,5)};
            var heading=Text("Crosshair",32,true);heading.Margin=new Thickness(0,0,0,22);
            page.Children.Add(Compactable(heading,heading));
            // The preview and the saved crosshairs stay on the left; the folders that shape it, and the share codes, sit beside them.
            // On a narrow window everything stacks into one column.
            var left=new StackPanel();
            left.Children.Add(CrosshairPreviewCard());
            left.Children.Add(CrosshairGalleryCard());
            left.Children.Add(CrosshairLibrary());
            var right=new StackPanel();
            right.Children.Add(CrosshairShapeCard());
            right.Children.Add(CrosshairLookCard());
            right.Children.Add(CrosshairCodes());
            page.Children.Add(ResponsiveColumns(left,right,.8));
            crosshairSyncing=false;
            RefreshCrosshairViews();
            return page;
        }
        void RefreshCrosshairViews(){foreach(var view in crosshairViews.ToArray())view();}
        // Every change updates the previews, is saved, and reaches the crosshair on screen at once.
        void CrosshairChanged(bool custom)
        {
            if(crosshairSyncing)return;
            if(custom)CrosshairLook.Name="Custom";
            RefreshCrosshairViews();
            SaveSoon();
            if(crosshair!=null)ConfigureCrosshair();
        }
        // After a style, a saved look or an import replaces the settings, every control shows the new values.
        void SyncCrosshairControls()
        {
            crosshairSyncing=true;
            try{foreach(var sync in crosshairSync.ToArray())sync();}
            finally{crosshairSyncing=false;}
        }
        // Just the crosshair on a plain dark stage, and the button that puts it on the screen.
        FrameworkElement CrosshairPreviewCard()
        {
            var card=Section("Preview","");
            var stage=new Border {Height=150,CornerRadius=new CornerRadius(12),Margin=new Thickness(0,8,0,12),ClipToBounds=true,Background=new SolidColorBrush(Color.FromRgb(22,22,24))};
            crosshairPreview=PixelPicture();
            stage.Child=crosshairPreview;
            card.Children.Add(stage);
            crosshairButton=Action(crosshair==null?"Turn crosshair on":"Turn crosshair off",ToggleCrosshair);
            card.Children.Add(crosshairButton);
            card.Children.Add(CrosshairMonitorChoices());
            crosshairViews.Add(()=>
            {
                var spec=CrosshairLook;
                int side=CrosshairRenderer.Extent(spec)*2+1,zoom=Math.Max(1,Math.Min(4,110/side));
                crosshairPreview.Source=RenderCrosshairPreview(spec,side);
                crosshairPreview.Width=crosshairPreview.Height=side*zoom;
            });
            return card;
        }
        // A card that opens and closes like the Outline one, with its own arrow.
        FrameworkElement Folder(string title,FrameworkElement body,bool open)
        {
            var folder=new Expander {Header=title,Content=body,IsExpanded=open,Foreground=Brushes.White,FontSize=14,Style=(Style)Resources["FolderExpander"],Tag="Folder"};
            return new Border {Background=cardSurface,CornerRadius=new CornerRadius(16),Margin=new Thickness(0,0,0,14),Child=Design.Glass(folder,16),Tag=title};
        }
        Image PixelPicture()
        {
            var picture=new Image {HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);
            return picture;
        }
        // A small picture of a look in a dark box, drawn without moving it off center.
        void ShowCrosshairPicture(Image picture,CrosshairSettings spec,int box)
        {
            int side=CrosshairRenderer.Extent(spec)*2+1;
            var drawn=RenderCrosshairPreview(spec,side);
            var source=drawn as BitmapSource;
            if(source==null){picture.Source=drawn;int z=Math.Max(1,Math.Min(3,box/side));picture.Width=picture.Height=side*z;return;}
            // Cut to the pixels that are drawn, so the picture is centered on what you see. Drawn around its aiming pixel, a shape an even number of
            // pixels wide (a 4 pixel dot) sat half a pixel off, and enlarged three times that showed.
            try
            {
                var pixels=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
                int w=pixels.PixelWidth,h=pixels.PixelHeight;var data=new byte[w*h*4];pixels.CopyPixels(data,w*4,0);
                int left=w,top=h,right=-1,bottom=-1;
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(data[(y*w+x)*4+3]>0){if(x<left)left=x;if(x>right)right=x;if(y<top)top=y;if(y>bottom)bottom=y;}
                if(right>=left&&bottom>=top){var cut=new CroppedBitmap(pixels,new Int32Rect(left,top,right-left+1,bottom-top+1));cut.Freeze();source=cut;}
            }
            catch(Exception){}
            int zoom=Math.Max(1,Math.Min(3,box/Math.Max(source.PixelWidth,source.PixelHeight)));
            picture.Source=source;
            picture.Width=source.PixelWidth*zoom;picture.Height=source.PixelHeight*zoom;
        }
        // The side of the dark box around a picture: about this size, a pixel more where needed so the room on each side is a whole number of pixels.
        static double EvenSide(double about,double picture){return ((int)Math.Round(about-picture))%2==0?about:about+1;}
        // Ready-made looks to try with one click, and a picture (PNG) of your own as the crosshair.
        readonly List<KeyValuePair<Border,string>> galleryTiles=new List<KeyValuePair<Border,string>>();
        FrameworkElement CrosshairGalleryCard()
        {
            galleryTiles.Clear();
            var card=new StackPanel();
            var hint=Text("Crosshairs the pros use. Click one to try it.",13);hint.Foreground=Muted;card.Children.Add(hint);
            // Even columns that fill the card: all six in a row when they fit, otherwise three or two to a row, so none is left alone at the end.
            var tiles=new System.Windows.Controls.Primitives.UniformGrid {Margin=new Thickness(-3,8,-3,4),Columns=6};
            tiles.SizeChanged+=(s,e)=>{int count=Math.Max(1,tiles.Children.Count);int fit=Math.Max(1,(int)(e.NewSize.Width/78));int columns=fit>=count?count:fit>=3?3:2;if(tiles.Columns!=columns)tiles.Columns=columns;};
            foreach(var style in CrosshairGallery.Looks)
            {
                var look=style;
                var picture=PixelPicture();ShowCrosshairPicture(picture,look,44);
                var box=new Border {Width=EvenSide(52,picture.Width),Height=EvenSide(52,picture.Height),CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Color.FromArgb(80,0,0,0)),Child=picture,HorizontalAlignment=HorizontalAlignment.Center};
                var content=new StackPanel();content.Children.Add(box);
                content.Children.Add(new TextBlock {Text=look.Name,FontSize=11,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=72,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,0)});
                var pick=Action("",()=>
                {
                    // The style replaces the shape and colors; where you moved the crosshair to stays.
                    var next=look.Copy();next.OffsetX=CrosshairLook.OffsetX;next.OffsetY=CrosshairLook.OffsetY;next.Opacity=CrosshairLook.Opacity;
                    preferences.Crosshair=next;SyncCrosshairControls();CrosshairChanged(false);
                    SetStatus("Trying "+look.Name);
                });
                pick.Content=content;pick.Style=(Style)Resources["PlainAction"];pick.Padding=new Thickness(6,8,6,6);pick.Margin=new Thickness(0);
                // The whole tile is the button, with the crosshair and its name in the middle.
                pick.HorizontalAlignment=HorizontalAlignment.Stretch;pick.HorizontalContentAlignment=HorizontalAlignment.Center;content.HorizontalAlignment=HorizontalAlignment.Center;
                pick.Tag=look.Name;System.Windows.Automation.AutomationProperties.SetName(pick,"Try "+look.Name);
                var tile=new Border {Margin=new Thickness(3,0,3,6),CornerRadius=new CornerRadius(10),Background=optionSurface,Child=pick};
                galleryTiles.Add(new KeyValuePair<Border,string>(tile,look.Name));
                tiles.Children.Add(tile);
            }
            card.Children.Add(tiles);
            // A picture of your own: a PNG becomes the crosshair, drawn pixel for pixel at the size you pick.
            var pictureRow=new WrapPanel {Margin=new Thickness(0,6,0,0)};
            pictureRow.Children.Add(Action("Use a picture (PNG)…",ImportCrosshairPicture));
            // The picture's overall size, any step from a quarter to four times its own pixels.
            var sizes=CrosshairSlider("Size",25,400,()=>CrosshairLook.ImageScale,v=>CrosshairLook.ImageScale=v,"%");
            var remove=Action("Remove picture",()=>{CrosshairLook.Image="";CrosshairLook.Name="Custom";CrosshairChanged(false);SetStatus("Back to the drawn crosshair");});
            pictureRow.Children.Add(remove);
            card.Children.Add(pictureRow);
            var sizeLabel=Text("Picture",12,true);sizeLabel.Foreground=Muted;sizeLabel.Margin=new Thickness(0,8,0,0);
            card.Children.Add(sizeLabel);card.Children.Add(sizes);
            crosshairViews.Add(()=>
            {
                bool hasPicture=CrosshairLook.HasImage;
                foreach(var tile in galleryTiles)tile.Key.Background=!hasPicture&&tile.Value==CrosshairLook.Name?selectionSurface:optionSurface;
                remove.Visibility=sizes.Visibility=sizeLabel.Visibility=hasPicture?Visibility.Visible:Visibility.Collapsed;
            });
            return Folder("Try a style",card,true);
        }
        void ImportCrosshairPicture()
        {
            if(TestMode)return;
            var open=new Microsoft.Win32.OpenFileDialog {Filter="PNG picture|*.png",Title="Choose a crosshair picture"};
            if(open.ShowDialog()!=true)return;
            try
            {
                int w,h;
                using(var probe=new System.Drawing.Bitmap(open.FileName)){w=probe.Width;h=probe.Height;}
                if(w>512||h>512){Message("Pick a picture up to 512 × 512 pixels. Crosshair pictures are usually small, like 64 × 64.");return;}
                // A copy is kept in Lucent's folder, so the crosshair keeps working if the original file moves.
                string folder=Path.Combine(StateStore.Folder,"crosshairs");Directory.CreateDirectory(folder);
                byte[] bytes=File.ReadAllBytes(open.FileName);
                string name;using(var sha=System.Security.Cryptography.SHA1.Create())name=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").Substring(0,16).ToLowerInvariant()+".png";
                string target=Path.Combine(folder,name);
                if(!File.Exists(target))File.WriteAllBytes(target,bytes);
                CrosshairLook.Image=target;CrosshairLook.ImageScale=100;
                string title=Path.GetFileNameWithoutExtension(open.FileName);CrosshairLook.Name=title.Length>24?title.Substring(0,24):title;
                CrosshairChanged(false);
                SetStatus("Your picture is the crosshair now");
            }
            catch(Exception){Message("That picture could not be read. Use a PNG file.");}
        }
        FrameworkElement CrosshairLibrary()
        {
            var card=new StackPanel();
            var save=new DockPanel {Margin=new Thickness(0,6,0,6)};
            var nameBox=new TextBox {MinHeight=36,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,10,4),MaxLength=24,ToolTip="Name this look"};
            System.Windows.Automation.AutomationProperties.SetName(nameBox,"Crosshair name");
            var saveButton=Action("Save current",()=>
            {
                string name=nameBox.Text.Trim();
                if(name.Length==0)name="My crosshair "+(preferences.SavedCrosshairs.Count+1);
                var copy=CrosshairLook.Copy();
                copy.Name=name;copy.Clamp();
                int at=preferences.SavedCrosshairs.FindIndex(x=>x.Name==copy.Name);
                if(at>=0)preferences.SavedCrosshairs[at]=copy;
                else
                {
                    if(preferences.SavedCrosshairs.Count>=24){SetStatus("You can keep up to 24 crosshairs. Delete one first.");return;}
                    preferences.SavedCrosshairs.Add(copy);
                }
                CrosshairLook.Name=copy.Name;
                nameBox.Text="";
                SaveSoon();
                RebuildSavedCrosshairs();
                RefreshCrosshairViews();
                SetStatus("Saved "+copy.Name);
            });
            DockPanel.SetDock(saveButton,Dock.Right);
            save.Children.Add(saveButton);
            save.Children.Add(nameBox);
            card.Children.Add(save);
            savedCrosshairs=new WrapPanel();
            card.Children.Add(savedCrosshairs);
            RebuildSavedCrosshairs();
            crosshairViews.Add(()=>
            {
                foreach(var tile in savedTiles)tile.Key.Background=tile.Value==CrosshairLook.Name?selectionSurface:optionSurface;
            });
            return Folder("Your crosshairs",card,false);
        }
        // Copying and pasting a crosshair as a short code, at the bottom of the page.
        FrameworkElement CrosshairCodes()
        {
            var card=new StackPanel();
            var hint=Text("Share a look as a short code, or paste a friend's.",13);
            hint.Foreground=Muted;
            card.Children.Add(hint);
            var share=new DockPanel {Margin=new Thickness(0,8,0,0)};
            var codeBox=new TextBox {MinHeight=36,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,10,4),ToolTip="Paste a crosshair code here, or copy yours"};
            System.Windows.Automation.AutomationProperties.SetName(codeBox,"Crosshair code");
            var copyButton=Action("Copy my code",()=>
            {
                if(CrosshairLook.HasImage){SetStatus("A picture crosshair cannot be shared as a code. Send the PNG instead.");return;}
                string code=CrosshairCode.Encode(CrosshairLook);
                codeBox.Text=code;
                if(TestMode)return;
                try{Clipboard.SetText(code);SetStatus("Crosshair code copied");}
                catch(System.Runtime.InteropServices.ExternalException){SetStatus("The clipboard is busy. The code is in the box.");}
            });
            var importButton=Action("Import",()=>
            {
                var imported=CrosshairCode.Decode(codeBox.Text);
                if(imported==null){SetStatus("That is not a crosshair code.");return;}
                preferences.Crosshair=imported;
                codeBox.Text="";
                SyncCrosshairControls();
                CrosshairChanged(false);
                SetStatus("Imported "+imported.Name);
            });
            DockPanel.SetDock(copyButton,Dock.Left);
            DockPanel.SetDock(importButton,Dock.Right);
            copyButton.Margin=new Thickness(0,4,10,4);
            share.Children.Add(copyButton);
            share.Children.Add(importButton);
            share.Children.Add(codeBox);
            card.Children.Add(share);
            return Folder("Crosshair codes",card,false);
        }        void RebuildSavedCrosshairs()
        {
            savedCrosshairs.Children.Clear();savedTiles.Clear();
            if(preferences.SavedCrosshairs.Count==0)
            {
                var empty=Text("Nothing saved yet. Build a look, name it, and press Save current.",12);
                empty.Foreground=Muted;
                savedCrosshairs.Children.Add(empty);
                return;
            }
            foreach(var saved in preferences.SavedCrosshairs.ToArray())
            {
                var item=saved;
                var picture=PixelPicture();
                var shown=item.Copy();shown.OffsetX=shown.OffsetY=0;
                ShowCrosshairPicture(picture,shown,56);
                var box=new Border {Width=EvenSide(64,picture.Width),Height=EvenSide(64,picture.Height),CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Color.FromArgb(80,0,0,0)),Child=picture,HorizontalAlignment=HorizontalAlignment.Center};
                var caption=new TextBlock {Text=item.Name,FontSize=12,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,7,0,2)};
                var delete=Action("Delete",()=>
                {
                    preferences.SavedCrosshairs.Remove(item);
                    SaveSoon();
                    RebuildSavedCrosshairs();
                });
                delete.Style=(Style)Resources["TextAction"];delete.FontSize=11;delete.MinHeight=0;delete.Padding=new Thickness(4,2,4,0);delete.Margin=new Thickness(0);
                delete.HorizontalAlignment=HorizontalAlignment.Center;
                System.Windows.Automation.AutomationProperties.SetName(delete,"Delete "+item.Name);
                var content=new StackPanel();
                content.Children.Add(box);content.Children.Add(caption);content.Children.Add(delete);
                var tile=new Border {Width=116,CornerRadius=new CornerRadius(12),Padding=new Thickness(8,10,8,8),Margin=new Thickness(0,0,8,8),Cursor=Cursors.Hand,Tag=item.Name,Child=content,Background=item.Name==CrosshairLook.Name?selectionSurface:optionSurface};
                tile.MouseLeftButtonUp+=(s,e)=>
                {
                    preferences.Crosshair=item.Copy();
                    SyncCrosshairControls();
                    CrosshairChanged(false);
                };
                savedTiles.Add(new KeyValuePair<Border,string>(tile,item.Name));
                savedCrosshairs.Children.Add(tile);
            }
        }
        // One setting as a labeled slider that follows the look and changes it.
        FrameworkElement CrosshairSlider(string label,int min,int max,Func<int> get,Action<int> set,string suffix)
        {
            var row=new Grid {Margin=new Thickness(0,1,0,1)};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(104)});
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(54)});
            var caption=new TextBlock {Text=label,FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
            var slider=new Slider {Minimum=min,Maximum=max,Value=get(),SmallChange=1,LargeChange=Math.Max(1,(max-min)/10),TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Height=34,Margin=new Thickness(6,0,10,0),VerticalAlignment=VerticalAlignment.Center};
            var number=new TextBlock {Text=get()+suffix,FontSize=13,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
            System.Windows.Automation.AutomationProperties.SetName(slider,"Crosshair "+label.ToLowerInvariant());
            slider.ValueChanged+=(s,e)=>
            {
                if(crosshairSyncing)return;
                int value=(int)Math.Round(slider.Value);
                number.Text=value+suffix;
                if(value==get())return;
                set(value);
                CrosshairChanged(true);
            };
            crosshairSync.Add(()=>{slider.Value=get();number.Text=get()+suffix;});
            row.Children.Add(caption);
            Grid.SetColumn(slider,1);row.Children.Add(slider);
            Grid.SetColumn(number,2);row.Children.Add(number);
            return row;
        }
        CheckBox CrosshairToggle(string label,Func<bool> get,Action<bool> set)
        {
            var box=Toggle(label,get(),value=>{set(value);CrosshairChanged(true);});
            box.Margin=new Thickness(0,8,0,6);
            crosshairSync.Add(()=>box.IsChecked=get());
            return box;
        }
        Button ArmChip(string label,Func<bool> get,Action<bool> set)
        {
            var chip=Action("",()=>{set(!get());CrosshairChanged(true);});
            chip.Padding=new Thickness(12,8,12,8);chip.FontSize=12;chip.Margin=new Thickness(0,0,6,6);
            System.Windows.Automation.AutomationProperties.SetName(chip,label+" arm");
            Action paint=()=>{chip.Content=(get()?"✓  ":"")+label;chip.Background=get()?selectionSurface:optionSurface;};
            chip.Click+=(s,e)=>paint();
            crosshairSync.Add(paint);
            paint();
            return chip;
        }
        static void SetSliderCaption(FrameworkElement row,string text)
        {
            var grid=row as Grid;if(grid==null)return;
            foreach(var child in grid.Children)if(child is TextBlock&&Grid.GetColumn((UIElement)child)==0){((TextBlock)child).Text=text;return;}
        }
        // A chain-link button: linked, the crosshair is the same along X and Y; broken, X and Y are set apart.
        Button CrosshairLinkChip()
        {
            var icon=new System.Windows.Shapes.Path {Stroke=Brushes.White,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=16,Height=16,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center};
            var label=new TextBlock {FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
            var content=new StackPanel {Orientation=Orientation.Horizontal};content.Children.Add(icon);content.Children.Add(label);
            Button chip=null;
            chip=Action("",()=>
            {
                // Unlinking starts Y where it is now, so nothing moves until a Y slider is used.
                if(CrosshairLook.Linked){CrosshairLook.LengthY=CrosshairLook.Length;CrosshairLook.GapY=CrosshairLook.Gap;}
                CrosshairLook.Linked=!CrosshairLook.Linked;
                SyncCrosshairControls();
                CrosshairChanged(true);
            });
            chip.Content=content;chip.Padding=new Thickness(12,8,12,8);chip.Margin=new Thickness(0,0,0,8);
            System.Windows.Automation.AutomationProperties.SetName(chip,"Link X and Y");
            Action paint=()=>
            {
                bool linked=CrosshairLook.Linked;
                // Two links joined, or pulled apart.
                icon.Data=Geometry.Parse(linked?"M9.5,14.5 L14.5,9.5 M11,6.5 L13,4.5 A3.5,3.5 0 0 1 19.5,11 L17.5,13 M13,17.5 L11,19.5 A3.5,3.5 0 0 1 4.5,13 L6.5,11"
                                                :"M11,6.5 L13,4.5 A3.5,3.5 0 0 1 19.5,11 L17.5,13 M13,17.5 L11,19.5 A3.5,3.5 0 0 1 4.5,13 L6.5,11 M4,4 L7,7 M20,20 L17,17");
                label.Text=linked?"X and Y linked":"X and Y separate";
                chip.ToolTip=linked?"The arms are the same along X and Y. Click to set X and Y apart.":"Left and right (X) and top and bottom (Y) have their own length and gap. Click to link them.";
                chip.Background=linked?optionSurface:selectionSurface;
            };
            crosshairSync.Add(paint);paint();
            return chip;
        }
        FrameworkElement CrosshairShapeCard()
        {
            var card=new StackPanel();
            var arms=new WrapPanel {Margin=new Thickness(0,6,0,4)};
            arms.Children.Add(ArmChip("Top",()=>CrosshairLook.Top,v=>CrosshairLook.Top=v));
            arms.Children.Add(ArmChip("Right",()=>CrosshairLook.Right,v=>CrosshairLook.Right=v));
            arms.Children.Add(ArmChip("Bottom",()=>CrosshairLook.Bottom,v=>CrosshairLook.Bottom=v));
            arms.Children.Add(ArmChip("Left",()=>CrosshairLook.Left,v=>CrosshairLook.Left=v));
            card.Children.Add(arms);
            // The link: on, one length and gap for all four arms; off, the left and right arms (X) and the top and bottom arms (Y) each have their own.
            card.Children.Add(CrosshairLinkChip());
            var lengthX=CrosshairSlider("Arm length",0,40,()=>CrosshairLook.Length,v=>CrosshairLook.Length=v,"");
            var lengthY=CrosshairSlider("Length Y",0,40,()=>CrosshairLook.LengthY,v=>CrosshairLook.LengthY=v,"");
            var gapX=CrosshairSlider("Gap",0,30,()=>CrosshairLook.Gap,v=>CrosshairLook.Gap=v,"");
            var gapY=CrosshairSlider("Gap Y",0,30,()=>CrosshairLook.GapY,v=>CrosshairLook.GapY=v,"");
            card.Children.Add(lengthX);card.Children.Add(lengthY);
            card.Children.Add(CrosshairSlider("Thickness",1,10,()=>CrosshairLook.Thickness,v=>CrosshairLook.Thickness=v,""));
            card.Children.Add(gapX);card.Children.Add(gapY);
            Action showAxes=()=>
            {
                bool split=!CrosshairLook.Linked;
                lengthY.Visibility=gapY.Visibility=split?Visibility.Visible:Visibility.Collapsed;
                SetSliderCaption(lengthX,split?"Length X":"Arm length");SetSliderCaption(gapX,split?"Gap X":"Gap");
            };
            crosshairSync.Add(showAxes);showAxes();
            card.Children.Add(CrosshairSlider("Turn",0,45,()=>CrosshairLook.Rotation,v=>CrosshairLook.Rotation=v,"°"));
            card.Children.Add(CrosshairToggle("Center dot",()=>CrosshairLook.Dot,v=>CrosshairLook.Dot=v));
            card.Children.Add(CrosshairSlider("Dot size",1,12,()=>CrosshairLook.DotSize,v=>CrosshairLook.DotSize=v,""));
            card.Children.Add(CrosshairToggle("Square dot",()=>CrosshairLook.SquareDot,v=>CrosshairLook.SquareDot=v));
            return Folder("Shape",card,true);
        }
        FrameworkElement CrosshairLookCard()
        {
            var card=new StackPanel();
            card.Children.Add(CrosshairSlider("Opacity",20,100,()=>CrosshairLook.Opacity,v=>CrosshairLook.Opacity=v,"%"));
            card.Children.Add(CrosshairSlider("Offset X",-80,80,()=>CrosshairLook.OffsetX,v=>CrosshairLook.OffsetX=v,""));
            card.Children.Add(CrosshairSlider("Offset Y",-80,80,()=>CrosshairLook.OffsetY,v=>CrosshairLook.OffsetY=v,""));
            var color=Text("Color",15,true);
            color.Margin=new Thickness(0,14,0,4);
            card.Children.Add(color);
            card.Children.Add(BuildColorEditor(()=>ParseColor(CrosshairLook.FillColor,Colors.White),value=>CrosshairLook.FillColor=CrosshairColors.Format(ToDrawing(value)),()=>CrosshairChanged(true),crosshairSync));
            var outlineBody=new StackPanel {Margin=new Thickness(0,12,0,0)};
            var outlineEditor=BuildColorEditor(()=>ParseColor(CrosshairLook.OutlineColor,Colors.Black),value=>CrosshairLook.OutlineColor=CrosshairColors.Format(ToDrawing(value)),()=>CrosshairChanged(true),crosshairSync);
            var outlineToggle=Toggle("Show outline",CrosshairLook.Outline,value=>
            {
                CrosshairLook.Outline=value;
                outlineEditor.Visibility=value?Visibility.Visible:Visibility.Collapsed;
                CrosshairChanged(true);
            });
            crosshairSync.Add(()=>{outlineToggle.IsChecked=CrosshairLook.Outline;outlineEditor.Visibility=CrosshairLook.Outline?Visibility.Visible:Visibility.Collapsed;});
            outlineEditor.Visibility=CrosshairLook.Outline?Visibility.Visible:Visibility.Collapsed;
            outlineBody.Children.Add(outlineToggle);
            outlineBody.Children.Add(CrosshairSlider("Outline width",1,3,()=>CrosshairLook.OutlineThickness,v=>CrosshairLook.OutlineThickness=v,""));
            outlineBody.Children.Add(outlineEditor);
            card.Children.Add(new Expander {Header="Outline",Content=outlineBody,Margin=new Thickness(0,16,0,4),Foreground=Brushes.White,FontSize=16,Style=(Style)Resources["InlineExpander"]});
            return Folder("Appearance",card,false);
        }

        FrameworkElement BuildColorEditor(Func<Color> getColor,Action<Color> setColor,Action changed,List<Action> sync=null)
        {
            var host=new StackPanel();
            var layout=new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(255)});
            host.Children.Add(layout);
            var field=new CrosshairColorField {Margin=new Thickness(0,0,24,0)};
            layout.Children.Add(field);
            var inputs=new StackPanel();Grid.SetColumn(inputs,1);layout.Children.Add(inputs);
            var sample=new Border {Height=24,CornerRadius=new CornerRadius(6),Margin=new Thickness(0,0,0,8)};
            inputs.Children.Add(sample);
            var channels=new UniformGrid {Columns=3};inputs.Children.Add(channels);
            var boxes=new List<TextBox>();
            foreach(string channel in new[]{"R","G","B"})
            {
                var column=new StackPanel {Margin=new Thickness(0,0,6,0)};column.Children.Add(Text(channel,11));
                var box=new TextBox {MaxLength=3,Padding=new Thickness(8),FontSize=14};
                System.Windows.Automation.AutomationProperties.SetName(box,"Crosshair "+channel);
                column.Children.Add(box);channels.Children.Add(column);boxes.Add(box);
            }
            var hex=new TextBox {MaxLength=7,Margin=new Thickness(0,8,6,0),Padding=new Thickness(8),FontSize=14};
            System.Windows.Automation.AutomationProperties.SetName(hex,"Crosshair hex color");inputs.Children.Add(hex);
            bool syncing=false;
            Action<Color,bool> applyColor=(color,fromField)=>
            {
                syncing=true;
                setColor(color);
                boxes[0].Text=color.R.ToString();
                boxes[1].Text=color.G.ToString();
                boxes[2].Text=color.B.ToString();
                hex.Text="#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2");
                foreach(var box in boxes)box.BorderBrush=new SolidColorBrush(Color.FromRgb(68,68,68));
                hex.BorderBrush=new SolidColorBrush(Color.FromRgb(68,68,68));
                sample.Background=new SolidColorBrush(color);if(!fromField)field.SetColor(color);
                syncing=false;changed();
            };
            field.Changed+=color=>applyColor(color,true);
            Action commitRgb=()=>
            {
                byte r,g,b;
                if(byte.TryParse(boxes[0].Text,out r)&&byte.TryParse(boxes[1].Text,out g)&&byte.TryParse(boxes[2].Text,out b))applyColor(Color.FromRgb(r,g,b),false);
                else applyColor(getColor(),false);
            };
            foreach(var box in boxes)
            {
                box.LostKeyboardFocus+=(sender,args)=>{if(!syncing)commitRgb();};
                box.KeyDown+=(sender,args)=>{if(args.Key==Key.Enter){commitRgb();args.Handled=true;}};
            }
            Action commitHex=()=>
            {
                string value=hex.Text.Trim().TrimStart('#');int rgb;
                if(value.Length==6&&int.TryParse(value,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out rgb))
                    applyColor(Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb),false);
                else applyColor(getColor(),false);
            };
            hex.LostKeyboardFocus+=(sender,args)=>{if(!syncing)commitHex();};
            hex.KeyDown+=(sender,args)=>{if(args.Key==Key.Enter){commitHex();args.Handled=true;}};
            var swatches=new WrapPanel {Margin=new Thickness(0,10,0,0)};host.Children.Add(swatches);
            foreach(var color in new[]{Colors.White,Colors.Black,Colors.Lime,Colors.Cyan,Colors.Yellow,Colors.Magenta,Colors.Red})
            {
                Color chosen=color;var swatch=Action("",()=>applyColor(chosen,false));
                swatch.Content=new Border {Width=22,Height=22,CornerRadius=new CornerRadius(11),Background=new SolidColorBrush(chosen)};
                swatch.Padding=new Thickness(7);swatch.ToolTip=chosen.ToString();
                System.Windows.Automation.AutomationProperties.SetName(swatch,"Crosshair color "+chosen.ToString());
                swatches.Children.Add(swatch);
            }
            if(sync!=null)sync.Add(()=>applyColor(getColor(),false));
            applyColor(getColor(),false);return host;
        }
        // The look drawn at its own size in its own bitmap, ready to enlarge without smoothing.
        static ImageSource RenderCrosshairPreview(CrosshairSettings spec,int side)
        {
            using(var bitmap=new System.Drawing.Bitmap(side,side))
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            using(var stream=new MemoryStream())
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                var centered=spec.Copy();
                centered.OffsetX=centered.OffsetY=0;
                CrosshairRenderer.Draw(graphics,new System.Drawing.Rectangle(0,0,side,side),centered);
                bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);
                stream.Position=0;
                var frame=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }
        static Color ParseColor(string value,Color fallback)
        {
            var parsed=CrosshairColors.Parse(value,System.Drawing.Color.FromArgb(fallback.R,fallback.G,fallback.B));
            return Color.FromRgb(parsed.R,parsed.G,parsed.B);
        }
        static System.Drawing.Color ToDrawing(Color color){return System.Drawing.Color.FromArgb(color.R,color.G,color.B);}
        void ConfigureCrosshair(){crosshair.Configure(CrosshairLook);}
        // With more than one display, a choice of which one the crosshair is drawn on.
        readonly List<Button> crosshairMonitorButtons=new List<Button>();
        FrameworkElement CrosshairMonitorChoices()
        {
            var box=new StackPanel {Margin=new Thickness(0,14,0,0)};
            crosshairMonitorButtons.Clear();
            var screens=System.Windows.Forms.Screen.AllScreens;
            if(screens.Length<2)return box;
            box.Children.Add(Step("Show the crosshair on"));
            var row=new WrapPanel();
            for(int i=0;i<screens.Length;i++)
            {
                var screen=screens[i];string device=screen.DeviceName;
                // "Display 1", "Display 2", each with a small monitor icon.
                var icon=Design.Icon("Monitor",16);icon.Margin=new Thickness(0,0,9,0);icon.VerticalAlignment=VerticalAlignment.Center;
                var label=new TextBlock {Text="Display "+(i+1),FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
                var content=new StackPanel {Orientation=Orientation.Horizontal};content.Children.Add(icon);content.Children.Add(label);
                var chip=Action("",()=>ChooseCrosshairMonitor(device));
                chip.Content=content;chip.ToolTip=screen.Bounds.Width+" \u00D7 "+screen.Bounds.Height+(screen.Primary?" (main display)":"");
                chip.Tag=device;chip.Padding=new Thickness(14,8,18,8);chip.FontSize=13;chip.MinHeight=0;chip.Margin=new Thickness(0,0,6,6);
                System.Windows.Automation.AutomationProperties.SetName(chip,"Crosshair on display "+(i+1));
                crosshairMonitorButtons.Add(chip);row.Children.Add(chip);
            }
            box.Children.Add(row);
            ShowCrosshairMonitor();
            return box;
        }
        // The saved display, or the main one when none is saved or it is not connected.
        string ChosenCrosshairMonitor()
        {
            var screens=System.Windows.Forms.Screen.AllScreens;
            foreach(var screen in screens)if(string.Equals(screen.DeviceName,preferences.CrosshairMonitor,StringComparison.OrdinalIgnoreCase))return screen.DeviceName;
            foreach(var screen in screens)if(screen.Primary)return screen.DeviceName;
            return "";
        }
        void ShowCrosshairMonitor(){string chosen=ChosenCrosshairMonitor();foreach(var chip in crosshairMonitorButtons)chip.SetResourceReference(Control.BackgroundProperty,Equals(chip.Tag,chosen)?"AdaptiveSelectionBrush":"AdaptiveControlBrush");}
        internal void ChooseCrosshairMonitor(string device)
        {
            preferences.CrosshairMonitor=device;
            SaveSoon();
            if(crosshair!=null)crosshair.SetMonitor(device);
            ShowCrosshairMonitor();
            SetStatus("Crosshair moved to that display");
        }
        Button crosshairButton;
        void ToggleCrosshair()
        {
            if(TestMode)return;
            if(crosshair==null){crosshair=new CrosshairOverlay();crosshair.SetMonitor(preferences.CrosshairMonitor);ConfigureCrosshair();crosshair.Show();Count(c=>c.CrosshairUses++);SetStatus("Crosshair on");}
            else{crosshair.Dispose();crosshair=null;SetStatus("Crosshair off");}
            crosshairButton.Content=crosshair==null?"Turn crosshair on":"Turn crosshair off";
            UpdateHome();
        }
    }
}
