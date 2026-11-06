let input = '';
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => {
  // [TODO: act on the JSON event in input]
  process.exit(0);
});
