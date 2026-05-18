const pty = require('node-pty');

const ptyProcess = pty.spawn('gemini', ['--output-format', 'json'], {
  name: 'xterm-color',
  cols: 80,
  rows: 30,
  cwd: process.env.PWD,
  env: process.env
});

ptyProcess.on('data', function(data) {
  console.log('OUT:', data);
});

ptyProcess.write('Hello\r');

setTimeout(() => {
  console.log('Sending second message...');
  ptyProcess.write('How are you?\r');
}, 10000);

setTimeout(() => {
  ptyProcess.write('exit\r');
}, 20000);
