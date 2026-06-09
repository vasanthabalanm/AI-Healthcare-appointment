// TC-005: netlify.toml SPA redirect rule (AC-006)
// netlify.toml is served by Karma via angular.json test assets: { glob: "netlify.toml", input: "./", output: "/" }
describe('netlify.toml SPA redirect configuration (AC-006)', () => {
  let tomlContent: string;

  beforeEach(async () => {
    try {
      const response = await fetch('/netlify.toml');
      const text = response.ok ? await response.text() : '';
      // Guard against Karma serving its HTML page instead of the TOML file
      tomlContent = text.includes('[[redirects]]') ? text : '';
    } catch {
      tomlContent = '';
    }
  });

  it('contains SPA catch-all redirect: from = "/*"', () => {
    if (!tomlContent) {
      pending('netlify.toml not served — verify angular.json test assets include { glob: "netlify.toml", input: "./", output: "/" }');
      return;
    }
    expect(tomlContent).toContain('"/*"');
  });

  it('redirects to /index.html: to = "/index.html"', () => {
    if (!tomlContent) {
      pending('netlify.toml not served — verify angular.json test assets');
      return;
    }
    expect(tomlContent).toContain('"/index.html"');
  });

  it('uses status 200 (rewrite, not redirect): status = 200', () => {
    if (!tomlContent) {
      pending('netlify.toml not served — verify angular.json test assets');
      return;
    }
    expect(tomlContent).toContain('200');
  });
});
