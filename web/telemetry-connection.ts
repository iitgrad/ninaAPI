// Own one socket and one retry timer. Local receipt time also detects half-open sockets.
export function createTelemetryConnection(options: {
  url: () => string;
  renew: () => Promise<unknown>;
  receive: (data: string) => boolean;
  state: (open: boolean) => void;
  socket?: (url: string) => WebSocket;
  now?: () => number;
  delay?: typeof setTimeout;
}) {
  let socket: WebSocket | null = null;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let retry = 1000, lastMessage = 0, lastResume = -Infinity, started = false, renewing = false;
  const now = options.now ?? Date.now;
  function retire() {
    const old = socket;
    socket = null;
    if (old) {
      old.onopen = old.onmessage = old.onclose = old.onerror = null;
      old.close();
    }
    options.state(false);
  }
  function schedule() {
    if (timer !== undefined || renewing) return;
    retire();
    timer = (options.delay ?? setTimeout)(() => {
      timer = undefined;
      void reconnect();
    }, retry);
    retry = Math.min(10000, retry * 2);
  }
  async function reconnect() {
    if (renewing) return;
    renewing = true;
    try {
      await options.renew();
      open();
    } catch {
      // Keep the same bounded backoff when authentication or the network is unavailable.
    } finally {
      renewing = false;
      if (!socket) schedule();
    }
  }
  function open() {
    try {
      const next = (options.socket ?? (url => new WebSocket(url)))(options.url());
      socket = next;
      lastMessage = now();
      next.onopen = () => { if (socket === next) options.state(true); };
      next.onmessage = event => {
        if (socket !== next) return;
        if (options.receive(String(event.data))) {
          lastMessage = now();
          retry = 1000;
        }
      };
      next.onclose = next.onerror = () => { if (socket === next) schedule(); };
    } catch { schedule(); }
  }
  return {
    start() { if (!started) { started = true; open(); } },
    check() {
      if (started && socket && now() - lastMessage >= 30000) schedule();
    },
    resume() {
      if (!started || renewing || timer !== undefined || now() - lastResume < 1000) return;
      lastResume = now();
      // A hidden tab can retain an apparently open socket. Replace it on return.
      retire();
      void reconnect();
    },
  };
}
