/**
 * 零依赖静态文件服务器（Node.js）
 *
 * 为什么必须走 HTTP：浏览器在 file:// 协议下会阻止 fetch，
 * 而本页面需要 fetch frames.json 与 .vti 帧文件，因此不能直接双击打开 index.html。
 *
 * 用法：
 *     node serve.js [port]
 *
 * 默认监听 8080，站点根目录为本文件所在目录的上一级（即 CFDEngine 项目根），
 * 因此页面里可以用 ../test/bin/x64/Release/net10.0/frames 访问到 demo 输出。
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const url = require('url');

const ROOT = path.resolve(__dirname, '..');
const PORT = Number(process.argv[2] || process.env.PORT || 8080);

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.vti': 'application/octet-stream',
  '.vtk': 'application/octet-stream',
  '.pvd': 'application/xml; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.map': 'application/json; charset=utf-8',
};

const server = http.createServer((req, res) => {
  let pathname = decodeURIComponent(url.parse(req.url).pathname);

  if (pathname === '/' || pathname === '') {
    res.writeHead(302, { Location: '/viz/index.html' });
    res.end();
    return;
  }

  // 目录穿越防护：解析后必须仍在 ROOT 之内
  const filePath = path.join(ROOT, pathname);
  if (!filePath.startsWith(ROOT)) {
    res.writeHead(403);
    res.end('Forbidden');
    return;
  }

  fs.stat(filePath, (err, stat) => {
    if (err || !stat.isFile()) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('404 Not Found: ' + pathname);
      return;
    }

    const type = MIME[path.extname(filePath).toLowerCase()] || 'application/octet-stream';
    res.writeHead(200, {
      'Content-Type': type,
      'Content-Length': stat.size,
      'Cache-Control': 'no-cache',
      'Access-Control-Allow-Origin': '*',
    });

    fs.createReadStream(filePath).pipe(res);
  });
});

server.listen(PORT, () => {
  console.log('');
  console.log('  CFDEngine 仿真结果查看器');
  console.log('  ------------------------------------------');
  console.log('  站点根目录: ' + ROOT);
  console.log('  访问地址  : http://localhost:' + PORT + '/viz/index.html');
  console.log('');
  console.log('  提示: 页面默认从 ../test/bin/x64/Release/net10.0/frames 读取帧；');
  console.log('        可用 ?frames=<相对路径> 覆盖，例如');
  console.log('        http://localhost:' + PORT + '/viz/index.html?frames=myrun/frames');
  console.log('  按 Ctrl+C 停止');
  console.log('');
});
