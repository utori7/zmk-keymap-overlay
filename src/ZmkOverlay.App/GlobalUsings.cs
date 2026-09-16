// トレイ常駐のために UseWindowsForms を有効にしている副作用で、
// System.Drawing / System.Windows.Forms の型が暗黙 using に入り、
// 同名の WPF の型とぶつかる。このアプリの UI は WPF なので、
// 曖昧になる名前はすべて WPF 側に寄せる。
// WinForms 側の型が要る箇所（TrayIcon）は完全修飾で書くこと。

global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using Clipboard = System.Windows.Clipboard;
global using Color = System.Windows.Media.Color;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using ComboBox = System.Windows.Controls.ComboBox;
global using FlowDirection = System.Windows.FlowDirection;
global using FontFamily = System.Windows.Media.FontFamily;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MessageBox = System.Windows.MessageBox;
global using Orientation = System.Windows.Controls.Orientation;
global using Pen = System.Windows.Media.Pen;
global using Point = System.Windows.Point;
global using Rectangle = System.Windows.Shapes.Rectangle;
global using Size = System.Windows.Size;
global using TextBox = System.Windows.Controls.TextBox;
global using VerticalAlignment = System.Windows.VerticalAlignment;
