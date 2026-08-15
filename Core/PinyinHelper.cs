using System;
using System.Collections.Generic;
using System.Text;

namespace launcher.Core
{
    // 汉字拼音转换工具：内嵌常用汉字拼音字典（约750字），用于搜索时的拼音/首字母模糊匹配。
    // 输入 "wx" 可命中 "微信"（拼音首字母 w+x），输入 "weixin" 可命中 "微信"（全拼匹配）。
    // 非中文字符保持原样（小写），不影响英文/数字搜索。
    public static class PinyinHelper
    {
        // 获取单个字符的拼音（无声调），未知字符返回该字符的小写形式
        public static string GetPinyin(char c)
        {
            string py;
            if (Dict.TryGetValue(c, out py)) return py;
            return c.ToString().ToLowerInvariant();
        }

        // 获取字符串中每个汉字的拼音首字母拼接，非中文字符保持原样（小写）
        // 例："微信" → "wx"，"Report.docx" → "report.docx"，"你好World" → "nhworld"
        public static string GetInitials(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                string py = GetPinyin(c);
                sb.Append(py.Length > 0 ? py[0].ToString() : "");
            }
            return sb.ToString();
        }

        // 获取字符串的完整拼音（无声调，音节间空格分隔），非中文字符保持原样
        // 例："微信" → "wei xin"，"你好World" → "ni hao world"
        public static string GetFullPinyin(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length * 3);
            foreach (char c in s)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(GetPinyin(c));
            }
            return sb.ToString();
        }

        // 判断 label 的拼音是否与 query 匹配（支持全拼和首字母）
        // 例：label="微信.exe", query="wx" → true（首字母匹配）
        // 例：label="微信.exe", query="weixin" → true（全拼包含匹配）
        // 例：label="微信.exe", query="wei" → true（全拼包含匹配）
        public static bool IsPinyinMatch(string label, string query)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(query)) return false;
            // 检查是否含有中文字符，没有则无需拼音匹配
            bool hasChinese = false;
            foreach (char c in label)
            {
                if (c >= 0x4E00 && c <= 0x9FFF) { hasChinese = true; break; }
            }
            if (!hasChinese) return false;

            string initials = GetInitials(label);
            if (initials.Contains(query)) return true;

            string full = GetFullPinyin(label).Replace(" ", "");
            if (full.Contains(query)) return true;

            return false;
        }

        // 获取字符的拼音首字母（大写），非中文返回该字符的大写形式
        public static char GetFirstLetter(char c)
        {
            string py = GetPinyin(c);
            if (py.Length > 0 && char.IsLetter(py[0]))
                return char.ToUpperInvariant(py[0]);
            return char.ToUpperInvariant(c);
        }

        // ---- 常用汉字拼音字典（约750字，覆盖日常文件名高频用字） ----
        // 格式：'字'="拼音"（无声调）；多音字取最常用读音
        private static readonly Dictionary<char, string> Dict = BuildDict(
            // A
            "啊=a;阿=a;啊=a;哎=ai;哀=ai;爱=ai;矮=ai;碍=ai;安=an;按=an;暗=an;岸=an;案=an;昂=ang;" +
            // B
            "八=ba;把=ba;爸=ba;吧=ba;拔=ba;白=bai;百=bai;败=bai;班=ban;般=ban;板=ban;版=ban;半=ban;办=ban;帮=bang;棒=bang;包=bao;报=bao;抱=bao;暴=bao;杯=bei;北=bei;备=bei;被=bei;背=bei;本=ben;笨=ben;崩=beng;泵=beng;蹦=beng;逼=bi;鼻=bi;比=bi;笔=bi;彼=bi;必=bi;避=bi;壁=bi;臂=bi;边=bian;编=bian;便=bian;变=bian;遍=bian;标=biao;表=biao;别=bie;冰=bing;并=bing;病=bing;波=bo;播=bo;伯=bo;博=bo;薄=bo;不=bu;布=bu;步=bu;部=bu;捕=bu;补=bu;" +
            // C
            "才=cai;材=cai;菜=cai;参=can;残=can;灿=can;仓=cang;藏=cang;操=cao;草=cao;层=ceng;曾=ceng;产=chan;铲=chan;场=chang;常=chang;长=chang;厂=chang;唱=chang;畅=chang;超=chao;朝=chao;车=che;彻=che;尘=chen;沉=chen;陈=chen;称=cheng;成=cheng;城=cheng;程=cheng;吃=chi;持=chi;迟=chi;尺=chi;齿=chi;翅=chi;充=chong;冲=chong;虫=chong;宠=chong;抽=chou;仇=chou;愁=chou;出=chu;初=chu;除=chu;处=chu;楚=chu;触=chu;川=chuan;穿=chuan;传=chuan;串=chuan;窗=chuang;床=chuang;创=chuang;吹=chui;春=chun;纯=chun;词=ci;此=ci;次=ci;刺=ci;从=cong;聪=cong;丛=cong;粗=cu;促=cu;村=cun;存=cun;错=cuo;" +
            // D
            "答=da;打=da;大=da;达=da;带=dai;代=dai;待=dai;袋=dai;单=dan;但=dan;弹=dan;当=dang;党=dang;挡=dang;到=dao;道=dao;的=de;得=de;灯=deng;等=deng;低=di;地=di;第=di;帝=di;弟=di;底=di;掉=diao;调=diao;爹=die;跌=die;顶=ding;定=ding;钉=ding;盯=ding;东=dong;动=dong;冬=dong;斗=dou;豆=dou;都=du;读=du;堵=du;肚=du;度=du;断=duan;段=duan;队=dui;对=dui;顿=dun;多=duo;朵=duo;夺=duo;躲=duo;堕=duo;" +
            // E
            "额=e;恶=e;饿=e;恩=en;而=er;儿=er;耳=er;二=er;" +
            // F
            "发=fa;法=fa;反=fan;饭=fan;犯=fan;范=fan;方=fang;房=fang;防=fang;仿=fang;访=fang;放=fang;非=fei;飞=fei;肥=fei;费=fei;废=fei;分=fen;坟=fen;粉=fen;奋=fen;份=fen;愤=fen;丰=feng;风=feng;封=feng;疯=feng;逢=feng;缝=feng;讽=feng;佛=fo;否=fou;夫=fu;服=fu;福=fu;父=fu;付=fu;负=fu;妇=fu;附=fu;复=fu;副=fu;富=fu;腹=fu;覆=fu;" +
            // G
            "该=gai;改=gai;盖=gai;干=gan;感=gan;敢=gan;刚=gang;钢=gang;港=gang;杠=gang;高=gao;搞=gao;告=gao;哥=ge;歌=ge;割=ge;革=ge;个=ge;各=ge;给=gei;根=gen;跟=gen;更=geng;工=gong;公=gong;功=gong;共=gong;供=gong;宫=gong;恭=gong;巩=gong;狗=gou;构=gou;够=gou;古=gu;骨=gu;股=gu;鼓=gu;谷=gu;故=gu;顾=gu;固=gu;瓜=gua;挂=gua;寡=gua;卦=gua;拐=guai;怪=guai;关=guan;官=guan;管=guan;馆=guan;惯=guan;冠=guan;观=guan;广=guang;光=guang;逛=guang;归=gui;规=gui;贵=gui;鬼=gui;柜=gui;滚=gun;棍=gun;国=guo;果=guo;过=guo;锅=guo;" +
            // H
            "哈=ha;还=hai;海=hai;害=hai;含=han;寒=han;韩=han;喊=han;汉=han;汗=han;旱=han;航=hang;行=hang;好=hao;号=hao;毫=hao;豪=hao;好=hao;喝=he;合=he;和=he;河=he;何=he;荷=he;核=he;黑=hei;很=hen;狠=hen;恨=hen;哼=heng;红=hong;宏=hong;洪=hong;鸿=hong;厚=hou;候=hou;后=hou;呼=hu;忽=hu;湖=hu;虎=hu;互=hu;户=hu;护=hu;花=hua;华=hua;划=hua;化=hua;画=hua;话=hua;怀=huai;坏=huai;欢=huan;还=huan;环=huan;换=huan;患=huan;荒=huang;黄=huang;皇=huang;慌=huang;晃=huang;回=hui;灰=hui;挥=hui;汇=hui;会=hui;惠=hui;毁=hui;慧=hui;混=hun;魂=hun;活=huo;火=huo;货=huo;获=huo;或=huo;惑=huo;" +
            // J
            "机=ji;鸡=ji;积=ji;基=ji;激=ji;及=ji;级=ji;急=ji;集=ji;即=ji;几=ji;己=ji;记=ji;计=ji;技=ji;际=ji;季=ji;济=ji;继=ji;家=jia;加=jia;甲=jia;假=jia;价=jia;架=jia;嫁=jia;尖=jian;间=jian;坚=jian;监=jian;兼=jian;减=jian;检=jian;简=jian;剪=jian;件=jian;建=jian;健=jian;将=jiang;江=jiang;讲=jiang;奖=jiang;降=jiang;交=jiao;叫=jiao;教=jiao;较=jiao;脚=jiao;角=jiao;觉=jiao;接=jie;街=jie;节=jie;结=jie;解=jie;界=jie;借=jie;介=jie;今=jin;金=jin;紧=jin;尽=jin;近=jin;进=jin;斤=jin;津=jin;禁=jin;经=jing;精=jing;京=jing;景=jing;警=jing;净=jing;静=jing;镜=jing;竟=jing;敬=jing;究=jiu;九=jiu;久=jiu;酒=jiu;旧=jiu;救=jiu;就=jiu;居=ju;举=ju;句=ju;具=ju;据=ju;距=ju;聚=ju;剧=ju;决=jue;绝=jue;觉=jue;角=jue;军=jun;均=jun;君=jun;" +
            // K
            "开=kai;凯=kai;看=kan;刊=kan;砍=kan;康=kang;抗=kang;考=kao;靠=kao;科=ke;可=ke;克=ke;客=ke;课=ke;刻=ke;肯=ken;坑=keng;空=kong;孔=kong;控=kong;口=kou;扣=kou;苦=ku;哭=ku;库=ku;裤=ku;块=kuai;快=kuai;宽=kuan;款=kuan;况=kuang;矿=kuang;狂=kuang;框=kuang;亏=kui;奎=kui;愧=kui;昆=kun;困=kun;扩=kuo;括=kuo;阔=kuo;" +
            // L
            "拉=la;啦=la;来=lai;兰=lan;蓝=lan;栏=lan;兰=lan;懒=lan;烂=lan;浪=lang;狼=lang;劳=lao;老=lao;牢=lao;乐=le;雷=lei;类=lei;累=lei;冷=leng;棱=leng;黎=li;离=li;李=li;里=li;理=li;力=li;历=li;立=li;利=li;例=li;丽=li;连=lian;联=lian;脸=lian;练=lian;恋=lian;炼=lian;良=liang;两=liang;亮=liang;量=liang;粮=liang;凉=liang;辆=liang;聊=liao;了=liao;料=liao;列=lie;烈=lie;裂=lie;猎=lie;林=lin;临=lin;邻=lin;灵=ling;领=ling;令=ling;另=ling;留=liu;流=liu;刘=liu;柳=liu;六=liu;龙=long;隆=long;楼=lou;漏=lou;路=lu;陆=lu;录=lu;绿=lv;旅=lv;律=lv;率=lv;轮=lun;论=lun;罗=luo;落=luo;罗=luo;络=luo;裸=luo;" +
            // M
            "妈=ma;麻=ma;马=ma;码=ma;吗=ma;买=mai;卖=mai;麦=mai;满=man;慢=man;漫=man;忙=mang;盲=mang;猫=mao;毛=mao;矛=mao;冒=mao;贸=mao;帽=mao;么=me;没=mei;美=mei;每=mei;门=men;们=men;闷=men;梦=meng;猛=meng;蒙=meng;米=mi;迷=mi;密=mi;蜜=mi;棉=mian;面=mian;苗=miao;秒=miao;妙=miao;民=min;敏=min;名=ming;明=ming;命=ming;摸=mo;模=mo;膜=mo;摩=mo;魔=mo;末=mo;莫=mo;默=mo;谋=mou;某=mou;母=mu;木=mu;目=mu;牧=mu;幕=mu;墓=mu;慕=mu;" +
            // N
            "拿=na;那=na;哪=na;纳=na;乃=nai;奶=nai;耐=nai;男=nan;南=nan;难=nan;囊=nang;脑=nao;闹=nao;内=nei;能=neng;泥=ni;你=ni;逆=ni;年=nian;念=nian;娘=niang;鸟=niao;尿=niao;捏=nie;您=nin;宁=ning;牛=niu;农=nong;浓=nong;女=nv;暖=nuan;" +
            // O/P
            "哦=o;偶=ou;欧=ou;爬=pa;怕=pa;拍=pai;排=pai;派=pai;盘=pan;判=pan;盼=pan;旁=pang;胖=pang;跑=pao;泡=pao;炮=pao;培=pei;陪=pei;赔=pei;佩=pei;盆=pen;朋=peng;碰=peng;棚=peng;捧=peng;批=pi;皮=pi;疲=pi;脾=pi;匹=pi;片=pian;偏=pian;篇=pian;骗=pian;飘=piao;票=piao;拼=pin;贫=pin;品=pin;平=ping;评=ping;凭=ping;瓶=ping;萍=ping;坡=po;泼=po;颇=po;破=po;迫=po;剖=pou;扑=pu;铺=pu;仆=pu;普=pu;谱=pu;" +
            // Q
            "七=qi;期=qi;齐=qi;其=qi;奇=qi;骑=qi;起=qi;气=qi;器=qi;弃=qi;汽=qi;千=qian;前=qian;钱=qian;铅=qian;签=qian;浅=qian;强=qiang;墙=qiang;抢=qiang;巧=qiao;桥=qiao;敲=qiao;切=qie;且=qie;亲=qin;琴=qin;勤=qin;青=qing;清=qing;情=qing;请=qing;轻=qing;庆=qing;穷=qiong;秋=qiu;求=qiu;球=qiu;区=qu;曲=qu;取=qu;去=qu;趣=qu;圈=quan;全=quan;权=quan;泉=quan;拳=quan;劝=quan;缺=que;确=que;群=qun;" +
            // R
            "然=ran;染=ran;让=rang;绕=rao;热=re;人=ren;任=ren;认=ren;忍=ren;仍=reng;日=ri;容=rong;荣=rong;融=rong;柔=rou;肉=rou;如=ru;入=ru;乳=ru;软=ruan;锐=rui;" +
            // S
            "三=san;散=san;色=se;杀=sha;沙=sha;傻=sha;山=shan;闪=shan;善=shan;伤=shang;商=shang;上=shang;尚=shang;少=shao;绍=shao;设=she;社=she;射=she;涉=she;深=shen;神=shen;审=shen;甚=shen;身=shen;生=sheng;声=sheng;省=sheng;胜=sheng;师=shi;十=shi;石=shi;时=shi;实=shi;使=shi;始=shi;世=shi;事=shi;是=shi;示=shi;试=shi;室=shi;视=shi;收=shou;手=shou;首=shou;守=shou;受=shou;瘦=shou;书=shu;输=shu;舒=shu;树=shu;数=shu;术=shu;束=shu;属=shu;鼠=shu;暑=shu;署=shu;刷=shua;耍=shua;衰=shuai;帅=shuai;双=shuang;爽=shuang;谁=shei;水=shui;睡=shui;税=shui;顺=shun;说=shuo;思=si;死=si;四=si;寺=si;似=si;松=song;送=song;宋=song;搜=sou;苏=su;素=su;速=su;诉=su;酸=suan;算=suan;虽=sui;随=sui;岁=sui;碎=sui;孙=sun;损=sun;所=suo;锁=suo;索=suo;" +
            // T
            "他=ta;她=ta;它=ta;台=tai;太=tai;态=tai;泰=tai;谈=tan;弹=tan;坦=tan;叹=tan;炭=tan;汤=tang;堂=tang;糖=tang;躺=tang;烫=tang;逃=tao;桃=tao;陶=tao;讨=tao;套=tao;特=te;疼=teng;腾=teng;提=ti;题=ti;体=ti;替=ti;天=tian;田=tian;甜=tian;挑=tiao;条=tiao;跳=tiao;贴=tie;铁=tie;听=ting;停=ting;庭=ting;通=tong;同=tong;统=tong;痛=tong;头=tou;投=tou;透=tou;突=tu;图=tu;土=tu;吐=tu;团=tuan;推=tui;退=tui;脱=tuo;拖=tuo;妥=tuo;" +
            // W
            "挖=wa;瓦=wa;歪=wai;外=wai;弯=wan;完=wan;玩=wan;碗=wan;万=wan;汪=wang;王=wang;网=wang;往=wang;望=wang;危=wei;微=wei;威=wei;伟=wei;为=wei;围=wei;位=wei;未=wei;味=wei;卫=wei;文=wen;闻=wen;问=wen;稳=wen;我=wo;握=wo;屋=wo;无=wu;吴=wu;五=wu;午=wu;武=wu;物=wu;务=wu;误=wu;雾=wu;" +
            // X
            "西=xi;吸=xi;希=xi;息=xi;习=xi;席=xi;洗=xi;喜=xi;系=xi;细=xi;下=xia;夏=xia;吓=xia;先=xian;仙=xian;鲜=xian;显=xian;险=xian;现=xian;线=xian;限=xian;乡=xiang;相=xiang;香=xiang;箱=xiang;详=xiang;响=xiang;想=xiang;向=xiang;象=xiang;像=xiang;小=xiao;笑=xiao;效=xiao;校=xiao;些=xie;写=xie;血=xie;谢=xie;鞋=xie;心=xin;新=xin;信=xin;星=xing;行=xing;形=xing;型=xing;醒=xing;兴=xing;幸=xing;性=xing;姓=xing;兄=xiong;胸=xiong;雄=xiong;熊=xiong;休=xiu;修=xiu;秀=xiu;袖=xiu;需=xu;许=xu;虚=xu;须=xu;续=xu;选=xuan;学=xue;雪=xue;血=xue;训=xun;迅=xun;压=ya;牙=ya;芽=ya;崖=ya;亚=ya;严=yan;言=yan;眼=yan;演=yan;验=yan;阳=yang;羊=yang;洋=yang;仰=yang;养=yang;样=yang;邀=yao;要=yao;药=yao;爷=ye;也=ye;业=ye;叶=ye;夜=ye;液=ye;一=yi;已=yi;以=yi;衣=yi;依=yi;医=yi;仪=yi;宜=yi;移=yi;疑=yi;椅=yi;义=yi;艺=yi;忆=yi;异=yi;意=yi;易=yi;益=yi;因=yin;阴=yin;音=yin;银=yin;引=yin;饮=yin;隐=yin;印=yin;应=ying;英=ying;营=ying;迎=ying;影=ying;映=ying;硬=ying;永=yong;勇=yong;用=yong;优=you;忧=you;由=you;油=you;游=you;友=you;有=you;又=you;右=you;于=yu;鱼=yu;余=yu;与=yu;雨=yu;语=yu;玉=yu;育=yu;域=yu;欲=yu;遇=yu;预=yu;元=yuan;园=yuan;原=yuan;源=yuan;远=yuan;院=yuan;愿=yuan;约=yue;月=yue;越=yue;阅=yue;云=yun;运=yun;允=yun;" +
            // Z
            "在=zai;再=zai;灾=zai;载=zai;早=zao;造=zao;则=ze;责=ze;择=ze;则=ze;贼=zei;怎=zen;增=zeng;赠=zeng;扎=zha;扎=zha;眨=zha;炸=zha;摘=zhai;宅=zhai;窄=zhai;债=zhai;沾=zhan;展=zhan;占=zhan;战=zhan;站=zhan;张=zhang;章=zhang;长=zhang;掌=zhang;涨=zhang;丈=zhang;帐=zhang;账=zhang;招=zhao;找=zhao;照=zhao;罩=zhao;者=zhe;这=zhe;着=zhe;真=zhen;针=zhen;珍=zhen;诊=zhen;阵=zhen;振=zhen;镇=zhen;争=zheng;正=zheng;政=zheng;整=zheng;证=zheng;之=zhi;支=zhi;知=zhi;织=zhi;直=zhi;值=zhi;职=zhi;指=zhi;纸=zhi;志=zhi;制=zhi;治=zhi;致=zhi;置=zhi;智=zhi;中=zhong;终=zhong;钟=zhong;重=zhong;种=zhong;众=zhong;州=zhou;周=zhou;洲=zhou;粥=zhou;轴=zhou;皱=zhou;宙=zhou;朱=zhu;珠=zhu;猪=zhu;竹=zhu;主=zhu;住=zhu;注=zhu;助=zhu;著=zhu;筑=zhu;抓=zhua;转=zhuan;专=zhuan;砖=zhuan;赚=zhuan;装=zhuang;庄=zhuang;状=zhuang;撞=zhuang;追=zhui;准=zhun;桌=zhuo;着=zhuo;资=zi;子=zi;自=zi;字=zi;总=zong;走=zou;奏=zou;租=zu;足=zu;族=zu;组=zu;阻=zu;祖=zu;最=zui;罪=zui;嘴=zui;尊=zun;遵=zun;昨=zuo;左=zuo;右=zuo;作=zuo;做=zuo;坐=zuo;座=zuo;"
        );

        private static Dictionary<char, string> BuildDict(string data)
        {
            var dict = new Dictionary<char, string>(800);
            foreach (var entry in data.Split(';'))
            {
                var kv = entry.Split('=');
                if (kv.Length == 2 && kv[0].Length == 1)
                    dict[kv[0][0]] = kv[1];
            }
            return dict;
        }
    }
}
