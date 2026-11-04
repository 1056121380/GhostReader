using GhostReader;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
internal static class UxBenchmark
{
 internal static int Run()
 {
 using var state=new AppState();var book=new Book{FilePath=Path.Combine(Settings.ConfigDir,"benchmark-fixture.txt"),Title="Synthetic 90k characters"};
 for(int i=0;i<300;i++)book.Lines.Add(new string('夜',300));state.OpenBook(book);
 var cfg=new Settings();using var reader=new ReaderWindow(state,cfg);reader.Create(200,200,480,320);
 var clock=Stopwatch.StartNew();reader.Redraw();double first=clock.Elapsed.TotalMilliseconds;
 var layout=(ReadingLayout)typeof(ReaderWindow).GetField("_layout",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reader);var line=layout.Lines[0];
 cfg.BackgroundOpacity=.2;clock.Restart();reader.ApplyStyles();reader.RebuildLayout();reader.Redraw();double opacity=clock.Elapsed.TotalMilliseconds;bool opacityReused=ReferenceEquals(line,layout.Lines[0]);
 clock.Restart();reader.Resize(480,420);double height=clock.Elapsed.TotalMilliseconds;bool heightReused=ReferenceEquals(line,layout.Lines[0]);
 var result=new{Characters=90000,WrappedLines=layout.Lines.Count,FullInitialRenderMs=first,OpacityPreviewMs=opacity,HeightResizeMs=height,OpacityWrapReused=opacityReused,HeightWrapReused=heightReused};
 Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return opacityReused&&heightReused?0:1;
 }
}
