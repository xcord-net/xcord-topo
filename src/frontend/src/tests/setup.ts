globalThis.fetch = vi.fn() as typeof globalThis.fetch;

afterEach(() => {
  vi.clearAllMocks();
});
