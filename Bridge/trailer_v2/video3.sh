ffmpeg -loglevel error -y -framerate 30 -i "../trailer_v3/f_%05d.jpg" \
 -loop 1 -framerate 30 -t 16 -i bg.png -loop 1 -framerate 30 -t 16 -i logo.png -loop 1 -framerate 30 -t 16 -i soon.png -loop 1 -framerate 30 -t 16 -i kfollow.png -loop 1 -framerate 30 -t 16 -i sub.png -loop 1 -framerate 30 -t 16 -i streak.png \
 -loop 1 -framerate 30 -t 16 -i kbg.png -loop 1 -framerate 30 -t 4 -i kbg.png -loop 1 -framerate 30 -t 4 -i kintro.png -loop 1 -framerate 30 -t 4 -i kpres.png -loop 1 -framerate 30 -t 4 -i streak.png \
 -filter_complex "
[0:v]trim=end_frame=2837,setpts=PTS-STARTPTS,format=yuv420p,setsar=1[main];
[1:v]format=rgb24,fade=t=in:st=0:d=1.4,noise=alls=9:allf=t+u,format=rgba[bg];
[2:v]format=rgba,fade=t=in:st=1.4:d=0.1:alpha=1,fade=t=out:st=7.1:d=0.3:alpha=1,scale=w='1920*(1+0.14*max(0,1-(t-1.4)/0.55)+0.035*min(1,max(0,(t-1.4)/6)))':h=-1:eval=frame[logo];
[3:v]format=rgba,fade=t=in:st=7.4:d=0.35:alpha=1,fade=t=out:st=11.1:d=0.3:alpha=1,scale=w='1920*(1+0.06*max(0,(t-7.4))/4)':h=-1:eval=frame[soon];
[7:v]format=rgba,fade=t=in:st=11.4:d=0.5:alpha=1[kb];
[4:v]format=rgba,fade=t=in:st=11.6:d=0.5:alpha=1,scale=w='1920*(1+0.04*max(0,(t-11.4))/4.6)':h=-1:eval=frame[fol];
[5:v]format=rgba,fade=t=in:st=2.3:d=0.5:alpha=1,fade=t=out:st=6.6:d=0.4:alpha=1[sub];
color=c=black:s=1920x1080:r=30:d=16,format=rgba[blk];
[blk][6:v]overlay=x='-1920+3840*(t-3.0)/1.2':y=0:format=auto:enable='between(t,3.0,4.2)'[sw];
color=c=white:s=1920x1080:r=30:d=16,format=rgba,fade=t=in:st=1.4:d=0.02:alpha=1,fade=t=out:st=1.42:d=0.45:alpha=1[fl];
[bg][logo]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:format=auto[a1];
[a1][soon]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:format=auto[a2];
[a2][kb]overlay=0:0:format=auto[a2b];
[a2b][fol]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:format=auto[a3];
[a3][sub]overlay=0:0:format=auto[a4];
[a4]format=rgb24[a4r];[sw]format=rgb24[swr];
[a4r][swr]blend=all_mode=screen:all_opacity=0.45,format=rgba[a5];
[a5][fl]overlay=0:0:format=auto,fade=t=out:st=15.4:d=0.6,format=yuv420p,setsar=1,trim=duration=16,setpts=PTS-STARTPTS[end];
[8:v]format=rgb24,fade=t=in:st=0:d=0.6,format=rgba[ib];
[9:v]format=rgba,fade=t=in:st=0.5:d=0.9:alpha=1,scale=w='1920*(0.93+0.07*min(1,t/3.5))':h=-1:eval=frame[il];
[10:v]format=rgba,fade=t=in:st=1.9:d=0.6:alpha=1[ip];
[ib][il]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:format=auto[i1];
[i1][ip]overlay=0:0:format=auto[i2];
[11:v]format=rgba,colorchannelmixer=aa=0.35[isw];
[i2][isw]overlay=x='-1920+3840*(t-1.2)/1.1':y=0:format=auto:enable='between(t,1.2,2.3)',fade=t=out:st=3.45:d=0.55,format=yuv420p,setsar=1,trim=duration=4,setpts=PTS-STARTPTS[intro];
[intro][main][end]concat=n=3:v=1:a=0[v]" -map "[v]" -r 30 -c:v libx264 -crf 17 -preset medium -pix_fmt yuv420p video_only3.mp4
