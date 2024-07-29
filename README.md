# GrpcNetTransport
在不支持http2平台中，让gRPC通过Scoket进行网络传输

# 感谢 RedpointGames 
该库修改自 [uet](https://github.com/RedpointGames/uet.git)下 :
[Redpoint.Concurrency](https://github.com/RedpointGames/uet/tree/main/UET/Redpoint.Concurrency)
[Redpoint.GrpcPipes.Abstractions](https://github.com/RedpointGames/uet/tree/main/UET/Redpoint.GrpcPipes.Abstractions)
[Redpoint.GrpcPipes.Transport.Tcp](https://github.com/RedpointGames/uet/tree/main/UET/Redpoint.GrpcPipes.Transport.Tcp)

# 阶段
当前仅在实验阶段，并未在任何项目中得到过应用

# 另一种尝试
我尝试过接入Kcp以在Udp下发送信息，它是可行的但是传输效率似乎不高