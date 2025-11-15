import http from 'http';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const LISTEN_IP = process.env.HOST || '0.0.0.0';
const HTTP_PORT = process.env.PORT || 3000;
console.log('Starting Node.js Web Server...');
const server = http.createServer((req, res) => {
  const url = req.url;
  console.log(`[Web Server] Request received for: ${url}`);

  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
  
  if (req.method === 'OPTIONS') {
    res.writeHead(200);
    res.end();
    return;
  }

  if (url.startsWith('/currentServer.txt')) {
    fs.readFile(path.join(__dirname, 'currentServer.txt'), (err, data) => {
      if (err) {
        console.error('[Web Server] ERROR: currentServer.txt not found!');
        res.writeHead(404);
        res.end('File not found.');
      } else {
        console.log('[Web Server] Sending currentServer.txt');
        res.writeHead(200, { 'Content-Type': 'text/plain' });
        res.end(data);
      }
    });
  
  } else if (url === '/savecheatcode.txt') {
    console.log('[Web Server] Sending savecheatcode.txt');
    res.writeHead(200, { 'Content-Type': 'text/plain' });
    res.end('Honorificabilitudinitatibus');
  
  } else if (url === '/crossdomain.xml') {
    console.log('[Web Server] Sending crossdomain.xml');
    res.writeHead(200, { 'Content-Type': 'text/xml' });
    res.end(`<?xml version="1.0"?>
<cross-domain-policy>
<allow-access-from domain="*" secure="false"/>
<site-control permitted-cross-domain-policies="all"/>
</cross-domain-policy>`);
  
  } else if (url === '/game.swf') {
    fs.readFile(path.join(__dirname, 'game.swf'), (err, data) => {
      if (err) {
        console.error('[Web Server] ERROR: game.swf not found!');
        res.writeHead(404);
        res.end('File not found.');
      } else {
        console.log('[Web Server] Sending game.swf');
        res.writeHead(200, { 'Content-Type': 'application/x-shockwave-flash' });
        res.end(data);
      }
    });

  } else if (url === '/KawaiRun2Launcher.exe' || url === '/KawaiRun2Launcher') {
    const exePath = path.join(__dirname, 'KawaiRun2Launcher.exe');
    fs.stat(exePath, (err, stats) => {
      if (err || !stats.isFile()) {
        console.error('[Web Server] ERROR: KawaiRun2Launcher.exe not found!');
        res.writeHead(404);
        res.end('File not found.');
        return;
      }

      console.log('[Web Server] Sending KawaiRun2Launcher.exe as download');
      res.writeHead(200, {
        'Content-Type': 'application/octet-stream',
        'Content-Disposition': 'attachment; filename="KawaiRun2Launcher.exe"',
        'Content-Length': stats.size
      });
      const stream = fs.createReadStream(exePath);
      stream.pipe(res);
    });

  } else if (url === '/') {
    fs.readFile(path.join(__dirname, 'kawairun2.html'), 'utf8', (err, data) => {
      if (err) {
        console.error('[Web Server] ERROR: kawairun2.html not found!');
        res.writeHead(404);
        res.end('File not found.');
      } else {
        console.log('[Web Server] Sending kawairun2.html');
        res.writeHead(200, { 'Content-Type': 'text/html' });
        res.end(data);
      }
    });
  
  } else if (url.startsWith('/images/')) {
    const fileName = url.substring('/images/'.length);
    const filePath = path.join(__dirname, 'images', fileName);
    
    fs.readFile(filePath, (err, data) => {
      if (err) {
        console.error(`[Web Server] ERROR: File not found: ${fileName}`);
        res.writeHead(404);
        res.end('Not Found');
      } else {
        const ext = path.extname(fileName).toLowerCase();
        const contentTypes = {
          '.html': 'text/html',
          '.css': 'text/css',
          '.js': 'application/javascript',
          '.json': 'application/json',
          '.png': 'image/png',
          '.jpg': 'image/jpeg',
          '.jpeg': 'image/jpeg',
          '.gif': 'image/gif',
          '.svg': 'image/svg+xml',
          '.txt': 'text/plain'
        };
        const contentType = contentTypes[ext] || 'application/octet-stream';
        
        console.log(`[Web Server] Sending file: ${fileName}`);
        res.writeHead(200, { 'Content-Type': contentType });
        res.end(data);
      }
    });
  
  } else {
    res.writeHead(404);
    res.end('Not Found');
  }
});

server.listen(HTTP_PORT, LISTEN_IP, () => {
  console.log(`[Web Server] HTTP server listening on ${LISTEN_IP}:${HTTP_PORT}.`);
});

server.on('error', (err) => {
  if (err.code === 'EACCES') {
    console.error(`[Web Server] Error: Port ${HTTP_PORT} is protected. Please run this script as an Administrator.`);
    process.exit(1);
  } else {
    console.error(`[Web Server] Error: ${err.message}`);
  }
});