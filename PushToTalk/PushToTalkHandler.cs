using Microsoft.Psi;
using H.Hooks;

namespace PushToTalk
{
    public class PushToTalkHandler : IProducer<bool>
    {
        public PushToTalkHandler(Pipeline p) 
        {
            this.pipeline = p;
            this.Out = p.CreateEmitter<bool>(this, nameof(this.Out));

            LowLevelKeyboardHook hook = new();

            hook.Up += (_, args) => Console.WriteLine($"{nameof(hook.Up)}: {args}");
            hook.Up += handleUp;
            hook.Down += (_, args) => Console.WriteLine($"{nameof(hook.Down)}: {args}");
            hook.Down += handleDown;

            hook.Start();
            Console.WriteLine("Starting PtT");
        }

        private void handleUp(object? sender, KeyboardEventArgs args)
        {
            if (args.CurrentKey.Equals(Key.Return))
            {
                Out.Post(false, pipeline.GetCurrentTime());
            }
        }

        private void handleDown(object? sender, KeyboardEventArgs args)
        {
            if (args.CurrentKey.Equals(Key.Return))
            {
                Out.Post(true, pipeline.GetCurrentTime());
            }
        }

        public Emitter<bool> Out { get; private set; }

        private Pipeline pipeline;

        private bool talking = false;

        public static void Main(string[] args)
        {
            using (var p = Pipeline.Create())
            {
                var push = new PushToTalkHandler(p);
                var g = Generators.Repeat(p, '.', TimeSpan.FromSeconds(1));
                
                g.Do(x => Console.Write(x));
                push.Do(x => Console.Write(x));

                p.Run();
            }
        }
    }

}