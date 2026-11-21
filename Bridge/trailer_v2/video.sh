ffmpeg -loglevel error -y -framerate 30 -i "../trailer_classic/f_%05d.jpg" \
 -loop 1 -framerate 30 -t 16 -i bg.png -loop 1 -framerate 30 -t 16 -i logo.png -loop 1 -framerate 30 -t 16 -i soon.png -loop 1 -framerate 30 -t 16 -i follow.png -loop 1 -framerate 30 -t 16 -i sub.png -loop 1 -framerate 30 -t 16 -i streak.png \
 -filter_complex "
[0:v]trim=end_frame=1402,setpts=PTS-STARTPTS,fade=t=out:st=46.2:d=0.53,format=yuv420p,setsar=1[main];
[1:v]format=rgb24,fade=t=in:st=0:d=1.4,noise=alls=9:allf=t+u,format=rgba[bg];
[2:v]format=rgba,fade=t=in:st=1.4:d=0.1:alpha=1,fade=t=out:st=7.1:d=0.3:alpha=1,scale=w='1920*(1+0.14*max(0,1-(t-1.4)/0.55)+0.035*min(1,max(0,(t-1.4)/6)))':h=-1:eval=frame[logo];
[3:v]format=rgba,fade=t=in:st=7.4:d=0.35:alpha=1,fade=t=out:st=11.1:d=0.3:alpha=1,scale=w='1920*(1+0.06*max(0,(t-7.4))/4)':h=-1:eval=frame[soon];
[4:v]format=rgba,fade=t=in:st=11.4:d=0.45:alpha=1,scale=w='1920*(1+0.04*max(0,(t-11.4))/4.6)':h=-1:eval=frame[fol];
[5:v]format=rgba,fade=t=in:st=2.3:d=0.5:alpha=1,fade=t=out:st=6.6:d=0.4:alpha=1[sub];
color=c=black:s=1920x1080:r=30:d=16,format=rgba[blk];
[blk][6:v]overlay=x='-1920+3840*(t-3.0)/1.2':y=0:format=auto:enable='between(t,3.0,4.2)'[sw];
color=c=white:s=1920x1080:r=30:d=16,format=rgba,fade=t=in:st=1.4:d=0.02:alpha=1,fade=t=out:st=1.42:d=0.45:alpha=1[fl];
[bg][logo]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:format=auto[a1];
[a1][soon]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:format=auto[a2];
[a2][fol]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:format=auto[a3];
[a3][sub]overlay=0:0:format=auto[a4];
[a4]format=rgb24[a4r];[sw]format=rgb24[swr];
[a4r][swr]blend=all_mode=screen:all_opacity=0.45,format=rgba[a5];
[a5][fl]overlay=0:0:format=auto,fade=t=out:st=15.4:d=0.6,format=yuv420p,setsar=1,trim=duration=16,setpts=PTS-STARTPTS[end];
[main][end]concat=n=2:v=1:a=0[v]" -map "[v]" -r 30 -c:v libx264 -crf 17 -preset medium -pix_fmt yuv420p video_only.mp4
