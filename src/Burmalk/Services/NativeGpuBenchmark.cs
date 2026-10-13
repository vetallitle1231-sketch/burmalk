using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Burmalk.Services;

internal sealed class GpuFrame
{
    public double Seconds, Fps;
    public string Renderer;
    public byte[] Pixels;
}
internal sealed class NativeGpuResult
{
    public double Seconds, Fps;
    public string Renderer;
    public bool Completed;
}
// Fixed offscreen framebuffer: no SwapBuffers, vsync, window size or WPF rendering callbacks.
// glFinish counts completed work. UI preview readback is excluded from the measured draw time.
internal static class NativeGpuBenchmark
{
    public const string Engine = "gl-raymarch-v2-1280x720-96";
    public const int Width = 1280, Height = 720;
    public static bool MatchesRenderer(string renderer, string model)
    {
        string Normalize(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "[^a-zA-Z0-9]", "").ToLowerInvariant().Replace("nvidia", "").Replace("amd", "").Replace("ati", "");
        string expected = Normalize(model); return expected.Length > 3 && Normalize(renderer).Contains(expected);
    }
    private const uint Framebuffer = 0x8D40, Texture = 0x0DE1;
    public static Task<NativeGpuResult> RunAsync(IProgress<GpuFrame> progress, CancellationToken token, Func<bool> safe, double duration = 120) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        IntPtr window = IntPtr.Zero, dc = IntPtr.Zero, context = IntPtr.Zero;
        uint fbo = 0, texture = 0, program = 0, shader = 0;
        try
        {
            window = CreateWindowEx(0, "STATIC", "Burmalk GPU context", 0x80000000, 0, 0, 16, 16, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (window == IntPtr.Zero) throw new InvalidOperationException("Не удалось создать GPU-контекст.");
            dc = GetDC(window);
            var pfd = new PixelFormat { Size = 40, Version = 1, Flags = 0x25, ColorBits = 32 };
            int format = ChoosePixelFormat(dc, ref pfd);
            if (format == 0 || !SetPixelFormat(dc, format, ref pfd)) throw new InvalidOperationException("Нет подходящего OpenGL-формата.");
            context = wglCreateContext(dc);
            if (context == IntPtr.Zero || !wglMakeCurrent(dc, context)) throw new InvalidOperationException("OpenGL недоступен.");
            string renderer = Marshal.PtrToStringAnsi(glGetString(0x1F01)) ?? "";
            if (renderer.Length == 0 || renderer.Contains("GDI Generic", StringComparison.OrdinalIgnoreCase) || renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) || renderer.Contains("software", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Аппаратный OpenGL-драйвер недоступен. Обнови драйвер видеокарты.");
            var genFbo = Proc<Gen>("glGenFramebuffers"); var bindFbo = Proc<Bind>("glBindFramebuffer");
            var attach = Proc<Attach>("glFramebufferTexture2D"); var check = Proc<Check>("glCheckFramebufferStatus");
            genFbo(1, out fbo); bindFbo(Framebuffer, fbo);
            glGenTextures(1, out texture); glBindTexture(Texture, texture);
            glTexParameteri(Texture, 0x2801, 0x2601); glTexParameteri(Texture, 0x2800, 0x2601);
            glTexImage2D(Texture, 0, 0x8058, Width, Height, 0, 0x1908, 0x1401, IntPtr.Zero);
            attach(Framebuffer, 0x8CE0, Texture, texture, 0);
            if (check(Framebuffer) != 0x8CD5) throw new InvalidOperationException("GPU не поддерживает framebuffer теста.");
            shader = Proc<CreateShader>("glCreateShader")(0x8B30);
            IntPtr source = Marshal.StringToHGlobalAnsi(Fragment);
            try { Proc<ShaderSource>("glShaderSource")(shader, 1, ref source, IntPtr.Zero); }
            finally { Marshal.FreeHGlobal(source); }
            Proc<One>("glCompileShader")(shader);
            Proc<GetParameter>("glGetShaderiv")(shader, 0x8B81, out int compiled);
            if (compiled == 0)
            {
                var log = new StringBuilder(4096); Proc<InfoLog>("glGetShaderInfoLog")(shader, log.Capacity, out _, log);
                throw new InvalidOperationException("GPU shader: " + log);
            }
            program = Proc<CreateProgram>("glCreateProgram")(); Proc<Two>("glAttachShader")(program, shader); Proc<One>("glLinkProgram")(program);
            Proc<GetParameter>("glGetProgramiv")(program, 0x8B82, out int linked);
            if (linked == 0) throw new InvalidOperationException("Не удалось собрать GPU-программу.");
            Proc<One>("glUseProgram")(program); int location = Proc<Location>("glGetUniformLocation")(program, "phase"); var uniform = Proc<Uniform>("glUniform1f");
            glViewport(0, 0, Width, Height);
            void Draw(float phase)
            {
                uniform(location, phase); glBegin(4); glVertex2f(-1, -1); glVertex2f(3, -1); glVertex2f(-1, 3); glEnd(); glFinish();
                if (glGetError() != 0) throw new InvalidOperationException("Ошибка GPU во время рендеринга.");
            }
            Draw(0); // Warmup and driver compilation excluded.
            var wall = Stopwatch.StartNew(); double measured = 0; long frames = 0; double nextPreview = 0;
            while (wall.Elapsed.TotalSeconds < duration)
            {
                token.ThrowIfCancellationRequested();
                if (safe != null && !safe()) throw new InvalidOperationException("GPU-тест остановлен: перегрев или устаревшие датчики.");
                long begin = Stopwatch.GetTimestamp(); Draw((float)wall.Elapsed.TotalSeconds);
                measured += (Stopwatch.GetTimestamp() - begin) / (double)Stopwatch.Frequency; frames++;
                if (wall.Elapsed.TotalSeconds >= nextPreview)
                {
                    byte[] pixels = new byte[Width * Height * 4];
                    glReadPixels(0, 0, Width, Height, 0x80E1, 0x1401, pixels); // BGRA, bottom-up
                    if (glGetError() != 0) throw new InvalidOperationException("Не удалось проверить изображение GPU.");
                    progress?.Report(new GpuFrame { Seconds = wall.Elapsed.TotalSeconds, Fps = frames / measured, Renderer = renderer, Pixels = pixels });
                    nextPreview = wall.Elapsed.TotalSeconds + 2;
                }
            }
            return new NativeGpuResult { Seconds = wall.Elapsed.TotalSeconds, Fps = frames / measured, Renderer = renderer, Completed = wall.Elapsed.TotalSeconds >= duration && frames > 0 };
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                try { if (program != 0) Proc<One>("glDeleteProgram")(program); if (shader != 0) Proc<One>("glDeleteShader")(shader); if (fbo != 0) Proc<Delete>("glDeleteFramebuffers")(1, ref fbo); if (texture != 0) glDeleteTextures(1, ref texture); } catch { }
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(context);
            }
            if (dc != IntPtr.Zero) ReleaseDC(window, dc); if (window != IntPtr.Zero) DestroyWindow(window);
        }
    }, token);
    private static T Proc<T>(string name) where T : Delegate
    {
        IntPtr p = wglGetProcAddress(name);
        if (p == IntPtr.Zero || p.ToInt64() == -1 || p.ToInt64() is 1 or 2 or 3) throw new InvalidOperationException("Драйвер не поддерживает " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }
    private const string Fragment = """
        #version 120
        uniform float phase;
        float shape(vec3 p) {
            float a=phase*0.27; mat2 rot=mat2(cos(a),-sin(a),sin(a),cos(a)); p.xz=rot*p.xz;
            float torus=length(vec2(length(p.xy)-1.03,p.z))-0.32;
            float wave=sin(p.x*17.0+phase)*sin(p.y*17.0-phase)*sin(p.z*17.0)*0.028;
            return torus+wave;
        }
        void main() {
            vec2 uv=(gl_FragCoord.xy-vec2(640.0,360.0))/720.0;
            vec3 ray=normalize(vec3(uv,-1.05)); vec3 p=vec3(0.0,0.0,3.2); float travel=0.0; float d=0.0;
            for(int i=0;i<96;i++) { p=vec3(0.0,0.0,3.2)+ray*travel; d=shape(p); travel=min(12.0,travel+max(d*0.72,0.0)); }
            vec3 background=mix(vec3(0.025,0.04,0.08),vec3(0.07,0.025,0.12),uv.y+0.5);
            vec3 c=background;
            if(travel<8.0) {
                vec2 e=vec2(0.004,0.0); vec3 n=normalize(vec3(shape(p+e.xyy)-shape(p-e.xyy),shape(p+e.yxy)-shape(p-e.yxy),shape(p+e.yyx)-shape(p-e.yyx)));
                float diffuse=max(dot(n,normalize(vec3(-0.7,0.8,1.0))),0.0); float rim=pow(1.0-abs(dot(n,-ray)),3.0);
                c=vec3(0.09,0.6,0.65)*(0.15+diffuse)+vec3(0.65,0.3,0.95)*rim;
            }
            gl_FragColor=vec4(c,1.0);
        }
        """;
    [StructLayout(LayoutKind.Sequential)] private struct PixelFormat { public ushort Size, Version; public uint Flags; public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift, AccumBits, AccumRed, AccumGreen, AccumBlue, AccumAlpha, DepthBits, StencilBits, AuxBuffers, LayerType, Reserved; public uint LayerMask, VisibleMask, DamageMask; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Gen(int count, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Delete(int count, ref uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Bind(uint target, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Attach(uint target, uint attachment, uint textureTarget, uint texture, int level);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Check(uint target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint CreateShader(uint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint CreateProgram();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void One(uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Two(uint a, uint b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ShaderSource(uint shader, int count, ref IntPtr source, IntPtr length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetParameter(uint value, uint name, out int result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate void InfoLog(uint value, int count, out int length, StringBuilder log);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Location(uint program, string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Uniform(int location, float value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr module, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormat format);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr dc, int number, ref PixelFormat format);
    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr dc, IntPtr rc);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr rc);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] private static extern IntPtr glGetString(uint name);
    [DllImport("opengl32.dll")] private static extern uint glGetError();
    [DllImport("opengl32.dll")] private static extern void glGenTextures(int n, out uint texture);
    [DllImport("opengl32.dll")] private static extern void glDeleteTextures(int n, ref uint texture);
    [DllImport("opengl32.dll")] private static extern void glBindTexture(uint target, uint texture);
    [DllImport("opengl32.dll")] private static extern void glTexParameteri(uint target, uint name, int value);
    [DllImport("opengl32.dll")] private static extern void glTexImage2D(uint target, int level, int format, int width, int height, int border, uint inputFormat, uint type, IntPtr data);
    [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32.dll")] private static extern void glBegin(uint mode);
    [DllImport("opengl32.dll")] private static extern void glVertex2f(float x, float y);
    [DllImport("opengl32.dll")] private static extern void glEnd();
    [DllImport("opengl32.dll")] private static extern void glFinish();
    [DllImport("opengl32.dll")] private static extern void glReadPixels(int x, int y, int w, int h, uint format, uint type, byte[] pixels);
}
