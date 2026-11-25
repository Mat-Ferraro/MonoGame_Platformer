"""Static integration/schema checks and repeated-sweep report test; not a C#/GPU test."""
from pathlib import Path
import re,csv,sys,subprocess,tempfile
root=Path(__file__).resolve().parents[1]
s=(root/'Source/Game1.cs').read_text();r=(root/'Source/Lighting/GameplayLightingRenderer.cs').read_text();k=(root/'PlatformerSandbox.Foundation/Presentation/Lighting/SharedVisibilityKey.cs').read_text()
assert 'AllPerfOptimizations = 127' in s
assert 'ReuseSharedMasks = PerfEnabled(32)' in s and 'PersistSharedMasks = PerfEnabled(64)' in s
assert 'SharedMaskBudgetBytes = 128L * 1024 * 1024' in r
assert 'HorizontalOnly != horizontalOnly' in r and 'GaussianVisibilityPass(batch, entry.Target, _individual!' in r
assert 'e.UsedFrame != _sharedFrame' in r and 'ClearSharedMasks();' in r
assert 'DeviceResetting += SharedMaskDeviceResetting' in r and 'DeviceResetting -= SharedMaskDeviceResetting' in r
assert 'Vertices.ToArray()' in k and 'StartsAtExit' in k
assert 'while (_perfFixedRemainder >= tick)' in s and 'UpdateFrame(new GameTime(_perfFixedTotal,TargetElapsedTime))' in s
assert s.index('RecordPerfFrame(_perfPreviousLightingActive)') < s.index('DrawFrame(gameTime);',s.index('protected override void Draw'))
header=re.search(r'File.WriteAllLines\(Path.Combine\(folder,name\),new\[\]\{"([^"]+)"\}',s).group(1).split(',')
assert len(header)==len(set(header))
# Count top-level C# arguments for the CSV row, ignoring commas inside parentheses and strings.
row=s[s.index('_phase21Benchmark.Add(string.Join(",",'):s.index('));',s.index('_phase21Benchmark.Add(string.Join(",",'))+2]
argtext=row.split('string.Join(",",',1)[1][:-1];depth=0;n=1;quoted=False
for c in argtext:
    if c=='"':quoted=not quoted
    if quoted:continue
    if c in '([{':depth+=1
    elif c in ')]}':depth-=1
    elif c==',' and depth==0:n+=1
assert n==len(header),(n,len(header))
part=s[s.index('private static readonly (int Mask, int Isolation)[] PerfSweep'):s.index('private static readonly (int Mask, int Isolation)[] PerfQuickSweep')]
cases=[(int(a),int(b)) for a,b in re.findall(r'\((\d+),(\d+)\)',part)]
with tempfile.TemporaryDirectory() as t:
    for i,(mask,iso) in enumerate(cases,1):
        path=Path(t)/f'20261125-000000-000-mode6-High-mask{mask}-isolation{iso}-case{i:02}.csv'
        row={x:0 for x in header};row.update(preceding_frame_ms=20 if iso else 40 if mask==0 else 30 if mask==32 else 25 if mask==127 else 35,width=1280,height=720,optimization_mask=mask,isolation=iso,freeze_lights=1,fixed_step=0,vsync=0)
        with path.open('w',newline='') as f:
            w=csv.DictWriter(f,fieldnames=header);w.writeheader();w.writerows([row]*120)
    result=subprocess.check_output([sys.executable,str(root/'Tools/phase21_compare_performance.py'),t],text=True)
    assert 'Shared GPU masks: +10.00 ms/frame' in result and 'All seven optimizations' in result and 'Reference drift range: 40.00' in result
    assert 'World drawing off' in result and 'fixed framework 0' in result
print(f'PASS: {len(header)} CSV fields, {len(cases)} repeated cases, cache policy wiring, fixed simulation pacing path, repeated report (synthetic). C#/GPU not executed.')
