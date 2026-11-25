// Prints the pandoc HTML to a letter-size PDF with a running header and page numbers.
const { chromium } = require('playwright');
(async () => {
  const [html, pdf] = process.argv.slice(2);
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto('file://' + require('path').resolve(html), { waitUntil: 'networkidle' });
  const small = 'font-family:Inter,Arial,sans-serif;font-size:7pt;color:#444;width:100%;margin:0 0.7in;';
  await page.pdf({
    path: pdf, format: 'Letter', printBackground: true, displayHeaderFooter: true,
    margin: { top: '0.75in', bottom: '0.8in', left: '0.7in', right: '0.7in' },
    headerTemplate: `<div style="${small}">MasterFlow User Manual</div>`,
    footerTemplate: `<div style="${small}text-align:right;">Page <span class="pageNumber"></span> of <span class="totalPages"></span></div>`,
  });
  await browser.close();
})();
