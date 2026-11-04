using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace GhostReader
{
    internal sealed class ChapterForm : Form
    {
        private readonly AppState _state;
        private readonly ListBox _list;
        private readonly TextBox _search;
        private readonly Label _summary;
        private readonly List<int> _indices=new List<int>();
        public ChapterForm(AppState state)
        {
            _state=state;Text="章节目录 — "+(state.Book?.Title??"");ClientSize=new Size(480,560);MinimumSize=new Size(360,360);
            Font=new Font("Microsoft YaHei UI",9f);StartPosition=FormStartPosition.CenterScreen;KeyPreview=true;
            var header=new Panel{Dock=DockStyle.Top,Width=ClientSize.Width,Height=78};
            header.Controls.Add(new Label{Text="搜索章节名称或序号 · 当前章以 ▶ 标记",Left=12,Top=10,Width=420});
            _search=new TextBox{Name="ChapterSearch",PlaceholderText="输入关键字筛选章节…",Left=12,Top=38,Width=326,Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right};
            var current=new Button{Name="LocateCurrentChapter",Text="定位当前章",Left=350,Top=36,Width=116,Height=28,Anchor=AnchorStyles.Right|AnchorStyles.Top};
            header.Controls.Add(_search);header.Controls.Add(current);
            _list=new ListBox{Name="ChapterList",Dock=DockStyle.Fill,IntegralHeight=false,Font=new Font("Microsoft YaHei UI",10f)};
            var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(12,0,12,0)};body.Controls.Add(_list);
            _summary=new Label{Name="ChapterSummary",Dock=DockStyle.Bottom,Height=28,Padding=new Padding(12,5,0,0)};
            var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=46,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
            var close=new Button{Text="关闭",Width=90,Height=28,DialogResult=DialogResult.Cancel};footer.Controls.Add(close);CancelButton=close;
            Controls.Add(body);Controls.Add(_summary);Controls.Add(footer);Controls.Add(header);
            _search.TextChanged+=(s,e)=>FilterChapters();
            _search.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){JumpTo();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Down&&_list.Items.Count>0){_list.Focus();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape&&_search.TextLength>0){_search.Clear();e.SuppressKeyPress=true;}};
            _list.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){JumpTo();e.SuppressKeyPress=true;}};
            _list.MouseDoubleClick+=(s,e)=>{if(_list.IndexFromPoint(e.Location)>=0)JumpTo();};
            current.Click+=(s,e)=>{_search.Clear();SelectOriginal(_state.CurrentChapterIndex);_list.Focus();};
            KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.F){_search.Focus();_search.SelectAll();e.SuppressKeyPress=true;}};
            FilterChapters();
        }
        private void FilterChapters()
        {
            int previous=_list.SelectedIndex>=0&&_list.SelectedIndex<_indices.Count?_indices[_list.SelectedIndex]:_state.CurrentChapterIndex;
            _list.BeginUpdate();_list.Items.Clear();_indices.Clear();string query=_search.Text.Trim();
            if(_state.Book!=null)for(int i=0;i<_state.Book.Chapters.Count;i++)
            {
                string title=_state.Book.Chapters[i].Title??"";
                if(query.Length>0&&!title.Contains(query,StringComparison.OrdinalIgnoreCase)&&!(i+1).ToString().Contains(query,StringComparison.OrdinalIgnoreCase))continue;
                _indices.Add(i);_list.Items.Add((i==_state.CurrentChapterIndex?"▶ ":"   ")+(i+1)+". "+title);
            }
            _list.EndUpdate();
            if(!SelectOriginal(previous)&&!SelectOriginal(_state.CurrentChapterIndex)&&_list.Items.Count>0)_list.SelectedIndex=0;
            int total=_state.Book?.Chapters.Count??0;
            _summary.Text=_indices.Count==0?"没有匹配章节（0/"+total+"）":_indices.Count+"/"+total+" 章 · 双击或 Enter 跳转";
        }
        private bool SelectOriginal(int original)
        {
            int index=_indices.IndexOf(original);if(index<0)return false;
            _list.SelectedIndex=index;_list.TopIndex=Math.Max(0,index-4);return true;
        }
        private void JumpTo()
        {
            int selected=_list.SelectedIndex;if(selected<0||selected>=_indices.Count)return;
            if(_state.GoToChapter(_indices[selected]))Close();
        }
    }
}
