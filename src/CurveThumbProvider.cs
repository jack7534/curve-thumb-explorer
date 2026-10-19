using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

 [assembly: AssemblyVersion("1.2.1.0")]

namespace CurveThumb
{
    [ComVisible(true), Guid("E357FCCD-A995-4576-B01F-234630154E96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha);
    }

    [ComVisible(true), Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        void Initialize(IStream pstream, STGM grfMode);
    }

    public enum WTS_ALPHATYPE { WTSAT_UNKNOWN = 0, WTSAT_RGB = 1, WTSAT_ARGB = 2 }
    public enum STGM { Read = 0, Write = 1, ReadWrite = 2 }

    [ComVisible(true), Guid("F22C8013-265B-4F6C-9669-8FA59F8EA6D0"), ClassInterface(ClassInterfaceType.None), ProgId("CurveThumb.ShellThumbnailProvider")]
    public sealed class ShellThumbnailProvider : IThumbnailProvider, IInitializeWithStream
    {
        private byte[] data;
        private IStream binaryStlStream;
        private uint binaryStlCount;
        private const int MaxFileBytes = 128 * 1024 * 1024;
        public void Initialize(IStream stream, STGM mode)
        {
            if (stream == null) throw new COMException("No file stream", unchecked((int)0x80004005));
            long length = -1;
            try { System.Runtime.InteropServices.ComTypes.STATSTG info; stream.Stat(out info, 1); length = info.cbSize; }
            catch { }
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] chunk = new byte[65536];
                IntPtr readPtr = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    byte[] header = new byte[84];
                    Marshal.WriteInt32(readPtr, 0);
                    stream.Read(header, header.Length, readPtr);
                    int headerLength = Marshal.ReadInt32(readPtr);
                    if (headerLength == 84)
                    {
                        uint declared = BitConverter.ToUInt32(header, 80);
                        long expected = 84L + (long)declared * 50;
                        if (declared > 0 && (length == expected || (length < 0 && expected > MaxFileBytes)))
                        {
                            binaryStlStream = stream;
                            binaryStlCount = declared;
                            return;
                        }
                    }
                    ms.Write(header, 0, headerLength);
                    while (true)
                    {
                        Marshal.WriteInt32(readPtr, 0);
                        stream.Read(chunk, chunk.Length, readPtr);
                        int read = Marshal.ReadInt32(readPtr);
                        if (read <= 0) break;
                        if (ms.Length + read > MaxFileBytes) throw new COMException("Model exceeds the thumbnail size limit", unchecked((int)0x800700DF));
                        ms.Write(chunk, 0, read);
                    }
                    data = ms.ToArray();
                }
                finally { Marshal.FreeHGlobal(readPtr); }
            }
        }

        public void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WTS_ALPHATYPE.WTSAT_RGB;
            if ((data == null || data.Length == 0) && binaryStlStream == null) throw new COMException("Model stream is empty", unchecked((int)0x80004005));
            uint requested = Math.Max(32u, Math.Min(1024u, cx));
            int size = (int)requested;
            Bitmap rendered;
            try
            {
                if (binaryStlStream != null) rendered = StlRasterizer.Render(binaryStlStream, binaryStlCount, size);
                else { string badge; List<Tri> triangles = ModelReader.Read(data, out badge); rendered = MeshRenderer.Render(triangles, size, badge); }
            }
            catch { throw new COMException("Unsupported or invalid 3D mesh", unchecked((int)0x80004005)); }
            using (Bitmap bitmap = rendered)
            {
                phbmp = bitmap.GetHbitmap(Color.FromArgb(0, 0, 0));
                pdwAlpha = WTS_ALPHATYPE.WTSAT_RGB;
            }
        }
    }

    internal struct V3
    {
        public double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static V3 Cross(V3 a,V3 b) { return new V3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
        public double Length { get { return Math.Sqrt(X*X+Y*Y+Z*Z); } }
    }
    internal struct Tri { public V3 A,B,C; public Tri(V3 a,V3 b,V3 c){A=a;B=b;C=c;} }

    internal static class ModelReader
    {
        private const int MaxTriangles = 80000;
        private static string Ascii(byte[] bytes) { return Encoding.UTF8.GetString(bytes); }
        private static double D(string s) { double v; return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0; }
        private static void Add(List<Tri> t, V3 a,V3 b,V3 c){ if(t.Count<MaxTriangles)t.Add(new Tri(a,b,c)); }
        public static List<Tri> Read(byte[] b,out string badge)
        {
            if (b.Length >= 84)
            {
                uint declaredTriangles = BitConverter.ToUInt32(b, 80);
                if (declaredTriangles > 0 && 84L + (long)declaredTriangles * 50 == b.LongLength)
                {
                    badge = "STL";
                    return StlBinary(b);
                }
            }
            string text = Ascii(b);
            string head = text.Length > 32 ? text.Substring(0,32).ToLowerInvariant() : text.ToLowerInvariant();
            if (head.Contains("solid") && text.IndexOf("vertex",StringComparison.OrdinalIgnoreCase)>=0) { badge="STL"; return StlAscii(text); }
            if (text.StartsWith("ply")) { badge="PLY"; return Ply(text); }
            if (text.IndexOf("ISO-10303-21",StringComparison.OrdinalIgnoreCase)>=0) { badge="STEP"; throw new NotSupportedException("CAD tessellation needs an Open CASCADE kernel"); }
            if (text.IndexOf("IGES",StringComparison.OrdinalIgnoreCase)>=0) { badge="IGES"; throw new NotSupportedException("IGES surface tessellation is not enabled"); }
            if (text.IndexOf("PolygonVertexIndex:",StringComparison.Ordinal)>=0) { badge="FBX"; return FbxAscii(text); }
            if (b.Length>=4 && BitConverter.ToUInt32(b,0)==0x46546C67) { badge="GLB"; throw new NotSupportedException("GLB mesh reader is not enabled yet"); }
            if (text.TrimStart().StartsWith("{")) { badge="GLTF"; throw new NotSupportedException("glTF accessor reader is not enabled yet"); }
            if (text.IndexOf("<model",StringComparison.OrdinalIgnoreCase)>=0 || IsZip(b)) { badge="3MF"; return ThreeMf(b); }
            if (text.IndexOf("v ",StringComparison.Ordinal)>=0 && text.IndexOf("f ",StringComparison.Ordinal)>=0) { badge="OBJ"; return Obj(text); }
            if (b.Length>=84) { badge="STL"; return StlBinary(b); }
            throw new NotSupportedException();
        }
        private static bool IsZip(byte[] b){return b.Length>4&&b[0]==0x50&&b[1]==0x4B;}
        private static List<Tri> StlBinary(byte[] b)
        {
            uint n=BitConverter.ToUInt32(b,80); if(84L+n*50>b.Length)throw new InvalidDataException();
            List<Tri> t=new List<Tri>(); int stride=Math.Max(1,(int)(n/(MaxTriangles/1.0)));
            for(uint i=0;i<n&&t.Count<MaxTriangles;i+=(uint)stride){int o=84+(int)i*50+12;V3 a=ReadV(b,o),c=ReadV(b,o+12),d=ReadV(b,o+24);Add(t,a,c,d);}return t;
        }
        public static List<Tri> ReadBinarySampled(IStream stream, uint count, IntPtr readPtr)
        {
            const int maxSamples = 10000;
            int samples = (int)Math.Min((long)count, maxSamples);
            List<Tri> result = new List<Tri>(samples);
            byte[] record = new byte[50];
            for (int i = 0; i < samples; i++)
            {
                long index = samples == 1 ? 0 : (long)i * (count - 1) / (samples - 1);
                stream.Seek(84L + index * 50L, 0, IntPtr.Zero);
                Marshal.WriteInt32(readPtr, 0);
                stream.Read(record, record.Length, readPtr);
                if (Marshal.ReadInt32(readPtr) != record.Length) continue;
                result.Add(new Tri(ReadV(record, 12), ReadV(record, 24), ReadV(record, 36)));
            }
            if (result.Count == 0) throw new InvalidDataException("No STL triangles could be sampled");
            return result;
        }
        private static V3 ReadV(byte[] b,int o){return new V3(BitConverter.ToSingle(b,o),BitConverter.ToSingle(b,o+4),BitConverter.ToSingle(b,o+8));}
        private static List<Tri> StlAscii(string s)
        {
            MatchCollection m=Regex.Matches(s,@"vertex\s+([-+\d.eE]+)\s+([-+\d.eE]+)\s+([-+\d.eE]+)",RegexOptions.IgnoreCase);
            List<Tri> t=new List<Tri>(); int stride=Math.Max(1,m.Count/(MaxTriangles*3));
            for(int i=0;i+2<m.Count&&t.Count<MaxTriangles;i+=3*stride)Add(t,Vert(m[i]),Vert(m[i+1]),Vert(m[i+2]));return t;
        }
        private static V3 Vert(Match m){return new V3(D(m.Groups[1].Value),D(m.Groups[2].Value),D(m.Groups[3].Value));}
        private static List<Tri> Obj(string s)
        {
            List<V3> v=new List<V3>();List<Tri> t=new List<Tri>();
            foreach(string l in s.Split('\n')){string q=l.Trim();if(q.StartsWith("v ")){string[] a=q.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);if(a.Length>=4)v.Add(new V3(D(a[1]),D(a[2]),D(a[3])));}else if(q.StartsWith("f ")){string[] a=q.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);if(a.Length<4)continue;List<int> ids=new List<int>();for(int i=1;i<a.Length;i++){string z=a[i].Split('/')[0];int n;if(int.TryParse(z,out n))ids.Add(n>0?n-1:v.Count+n);}for(int i=1;i+1<ids.Count;i++)if(ids[0]>=0&&ids[i]>=0&&ids[i+1]>=0&&ids[0]<v.Count&&ids[i]<v.Count&&ids[i+1]<v.Count)Add(t,v[ids[0]],v[ids[i]],v[ids[i+1]]);}}return t;
        }
        private static List<Tri> Ply(string s)
        {
            int end=s.IndexOf("end_header",StringComparison.Ordinal);if(end<0)throw new InvalidDataException();string[] h=s.Substring(0,end).Split('\n');int nv=0,nf=0;bool ascii=false;foreach(string l in h){if(l.Contains("format ascii"))ascii=true;if(l.StartsWith("element vertex "))int.TryParse(l.Substring(15).Trim(),out nv);if(l.StartsWith("element face "))int.TryParse(l.Substring(13).Trim(),out nf);}if(!ascii)throw new NotSupportedException("Binary PLY is not enabled yet");string body=s.Substring(end+10).TrimStart('\r','\n');string[] rows=body.Split('\n');List<V3> v=new List<V3>();for(int i=0;i<nv&&i<rows.Length;i++){string[] a=rows[i].Split((char[])null,StringSplitOptions.RemoveEmptyEntries);if(a.Length>=3)v.Add(new V3(D(a[0]),D(a[1]),D(a[2])));}List<Tri> t=new List<Tri>();for(int i=nv;i<nv+nf&&i<rows.Length&&t.Count<MaxTriangles;i++){string[] a=rows[i].Split((char[])null,StringSplitOptions.RemoveEmptyEntries);if(a.Length<4)continue;int c;int.TryParse(a[0],out c);for(int j=1;j+1<c&&j+2<a.Length;j++){int x,y,z;if(int.TryParse(a[j],out x)&&int.TryParse(a[j+1],out y)&&int.TryParse(a[j+2],out z)&&x>=0&&y>=0&&z>=0&&x<v.Count&&y<v.Count&&z<v.Count)Add(t,v[x],v[y],v[z]);}}return t;
        }
        private static List<Tri> FbxAscii(string s)
        {
            Match vm=Regex.Match(s,@"Vertices:\s*\*\d+\s*\{\s*a:\s*([^}]*)\}",RegexOptions.Singleline);Match fm=Regex.Match(s,@"PolygonVertexIndex:\s*\*\d+\s*\{\s*a:\s*([^}]*)\}",RegexOptions.Singleline);if(!vm.Success||!fm.Success)throw new NotSupportedException("Binary FBX is not enabled yet");string[] raw=vm.Groups[1].Value.Split(',');List<V3> v=new List<V3>();for(int i=0;i+2<raw.Length;i+=3)v.Add(new V3(D(raw[i]),D(raw[i+1]),D(raw[i+2])));List<Tri> t=new List<Tri>();List<int> face=new List<int>();foreach(string token in fm.Groups[1].Value.Split(',')){int n;if(!int.TryParse(token.Trim(),out n))continue;bool end=n<0;if(end)n=~n;face.Add(n);if(end){for(int i=1;i+1<face.Count;i++)if(face[0]<v.Count&&face[i]<v.Count&&face[i+1]<v.Count)Add(t,v[face[0]],v[face[i]],v[face[i+1]]);face.Clear();}}return t;
        }
        private static List<Tri> ThreeMf(byte[] bytes)
        {
            using(MemoryStream ms=new MemoryStream(bytes))using(ZipArchive zip=new ZipArchive(ms,ZipArchiveMode.Read)){foreach(ZipArchiveEntry e in zip.Entries){if(!e.FullName.EndsWith(".model",StringComparison.OrdinalIgnoreCase))continue;XDocument x;using(Stream st=e.Open())x=XDocument.Load(st);XNamespace ns=x.Root.Name.Namespace;XElement resources=x.Root.Element(ns+"resources");if(resources==null)continue;foreach(XElement obj in resources.Elements(ns+"object")){XElement mesh=obj.Element(ns+"mesh");if(mesh==null)continue;List<V3> v=new List<V3>();XElement vertices=mesh.Element(ns+"vertices");foreach(XElement a in vertices.Elements(ns+"vertex"))v.Add(new V3(D((string)a.Attribute("x")),D((string)a.Attribute("y")),D((string)a.Attribute("z"))));List<Tri> t=new List<Tri>();foreach(XElement a in mesh.Element(ns+"triangles").Elements(ns+"triangle")){int i0=(int)a.Attribute("v1"),i1=(int)a.Attribute("v2"),i2=(int)a.Attribute("v3");if(i0<v.Count&&i1<v.Count&&i2<v.Count)Add(t,v[i0],v[i1],v[i2]);}if(t.Count>0)return t;}}}throw new InvalidDataException("No 3MF mesh");
        }
    }

    internal static class MeshRenderer
    {
        public static Bitmap Render(List<Tri> source,int size,string badge)
        {
            if(source==null||source.Count==0)throw new InvalidDataException("No triangles");
            int n=Math.Min(source.Count,80000);double[] min={double.MaxValue,double.MaxValue,double.MaxValue},max={double.MinValue,double.MinValue,double.MinValue};
            for(int i=0;i<n;i++)foreach(V3 v in new V3[]{source[i].A,source[i].B,source[i].C}){min[0]=Math.Min(min[0],v.X);min[1]=Math.Min(min[1],v.Y);min[2]=Math.Min(min[2],v.Z);max[0]=Math.Max(max[0],v.X);max[1]=Math.Max(max[1],v.Y);max[2]=Math.Max(max[2],v.Z);}
            double cx=(min[0]+max[0])/2,cy=(min[1]+max[1])/2,cz=(min[2]+max[2])/2,span=Math.Max(max[0]-min[0],Math.Max(max[1]-min[1],max[2]-min[2]));if(span<1e-12)span=1;
            float scale=(float)(size*.68/span);List<Face> faces=new List<Face>(n);V3 light=new V3(-.4,-.5,1);double ll=light.Length;
            for(int i=0;i<n;i++){Tri t=source[i];V3 normal=V3.Cross(t.B-t.A,t.C-t.A);double len=normal.Length;if(len<1e-12)continue;normal=new V3(normal.X/len,normal.Y/len,normal.Z/len);double shade=.38+.62*Math.Abs((normal.X*light.X+normal.Y*light.Y+normal.Z*light.Z)/ll);PointF a=Project(t.A,cx,cy,cz,scale,size),b=Project(t.B,cx,cy,cz,scale,size),c=Project(t.C,cx,cy,cz,scale,size);double depth=(-(t.A.X+t.A.Y+t.B.X+t.B.Y+t.C.X+t.C.Y)+(t.A.Z+t.B.Z+t.C.Z))/3;faces.Add(new Face(a,b,c,depth,shade));}
            faces.Sort((a,b)=>a.Depth.CompareTo(b.Depth));Bitmap bmp=new Bitmap(size,size,PixelFormat.Format32bppArgb);using(Graphics g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.FromArgb(255,36,40,48));using(Brush floor=new SolidBrush(Color.FromArgb(255,29,32,39)))g.FillEllipse(floor,size*.18f,size*.75f,size*.64f,size*.13f);foreach(Face f in faces){int val=(int)(155+100*f.Shade);using(Brush br=new SolidBrush(Color.FromArgb(255,Math.Min(255,val),Math.Min(255,(int)(val*.72)),Math.Min(255,(int)(val*.48)))))g.FillPolygon(br,new PointF[]{f.A,f.B,f.C});}using(Font font=new Font("Segoe UI",Math.Max(8,size*.075f),FontStyle.Bold,GraphicsUnit.Pixel)){SizeF m=g.MeasureString(badge,font);RectangleF box=new RectangleF(size-m.Width-size*.11f,size-m.Height-size*.1f,m.Width+size*.06f,m.Height+size*.045f);using(Brush bg=new SolidBrush(BadgeColor(badge)))g.FillRoundedRectangle(bg,box,size*.025f);using(Brush fg=new SolidBrush(Color.FromArgb(255,33,25,18)))g.DrawString(badge,font,fg,box.X+size*.03f,box.Y+size*.005f);}}
            return bmp;
        }
        private static Color BadgeColor(string badge)
        {
            switch((badge??"").ToUpperInvariant())
            {
                case "STL": return Color.FromArgb(238,249,115,22);
                case "OBJ": return Color.FromArgb(238,34,197,94);
                case "PLY": return Color.FromArgb(238,14,165,233);
                case "3MF": return Color.FromArgb(238,139,92,246);
                case "FBX": return Color.FromArgb(238,244,63,94);
                case "STEP": return Color.FromArgb(238,20,184,166);
                case "IGES": return Color.FromArgb(238,234,179,8);
                case "GLB": return Color.FromArgb(238,99,102,241);
                case "GLTF": return Color.FromArgb(238,168,85,247);
                default: return Color.FromArgb(238,255,133,49);
            }
        }
        private static PointF Project(V3 v,double cx,double cy,double cz,float scale,int size){double x=(v.X-cx),y=(v.Y-cy),z=(v.Z-cz);double px=(x-y)*.70710678,py=(x+y)*.408248+z*.816497;return new PointF((float)(size*.5+px*scale),(float)(size*.48-py*scale));}
        private struct Face{public PointF A,B,C;public double Depth,Shade;public Face(PointF a,PointF b,PointF c,double d,double s){A=a;B=b;C=c;Depth=d;Shade=s;}}
    }

    internal static class GraphicsCompat
    {
        public static void FillRoundedRectangle(this Graphics g,Brush b,RectangleF r,float radius){using(GraphicsPath p=new GraphicsPath()){float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();g.FillPath(b,p);}}
    }
}
