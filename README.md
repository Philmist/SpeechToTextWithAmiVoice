# SpeechToTextWithAmiVoice

���̃v���O�����͎����p�́u�䂩��˂��Ɓv�̂悤�ȉ����F���ǂݏグ�v���O�����ł��B

�ȉ��ɋ�����Z�p�̌��ؖړI�ō쐬����܂����B

- Avalonia ( https://avaloniaui.net/ )
- AmiVoice Cloud Platform ( https://acp.amivoice.com/main/ )
- Rx.NET ( https://github.com/dotnet/reactive )
- NAudio ( https://github.com/naudio/NAudio )

�G���[�����͂قƂ�ǂȂ���Ă��Ȃ��̂Ō��ؗp�Ɗ��肫���Ă���������ƍK���ł��B

## �g�p���@

�O��Ƃ���AmiVoice Cloud Platform�̏]�ʉۋ��_�񂪕K�v�ł��B
�����F���G���W���͓��{��-�ėp(`-a-general`)���g�p���Ă��܂��B

WebSocket��URI(�}�C�y�[�W�̐ڑ����)��APPKEY(�������ڑ����)����͂��āA
�}�C�N��I�ׂ΂Ƃ肠���������F���o����悤�ɂȂ�܂��B
APPKEY�͑��l�ɘR��Ă��܂��Ɩ�����������悤�Ȃ̂Œ��ӂ��Ă��������B
WASAPI��O��ɂ��Ă��邽�߁A�I�������}�C�N�ɂ���Ă͉�������肱�߂Ȃ����Ƃ�����܂��B

Profile ID�͒P���o�^�����ꍇ�ɕK�v�ƂȂ�܂��B
�}�j���A���ɂ�����܂���Web����P��o�^�����ꍇ��Profile ID��ݒ肵�Ă��������B
���[�U�[ID(�}�C�y�[�W�̂��o�^���e)��`hogehuga`�̏ꍇ�A
Profile ID��`:hogehuga`�Ɛݒ肵�Ă��������B

## �䂩��˂��ƓI�Ɏg�����߂̋@�\

�����ǂݏグ�͖_�ǂ݂����( https://chi.usamimi.info/Program/Application/BouyomiChan/ )��
�g�p���ēǂݏグ�܂��B
�_�ǂ݂���񑤂Œʏ��Socket�ł̓��͂�L�������ăA�h���X�ƃ|�[�g���Y���ӏ��ɓ��͂��Ă��������B
������Ԃ͖_�ǂ݂����̃f�t�H���g�̐ݒ�ɂȂ��Ă��܂��B

�������̕��@��OBS�Ɏ�����\�����邱�Ƃ��o���܂��B

FilePath�Ƀe�L�X�g�t�@�C�����w�肵�A
OBS���̃e�L�X�g�\�[�X�ŊY���e�L�X�g�t�@�C������͂Ƃ��Ďw�肷�邱�ƂŁA
������\�������邱�Ƃ��o���܂��B

��������TextSend Uri��HTTP POST�Ŏ󂯂����A�h���X���w�肷��ƁA
���̃A�h���X�ɑ΂���JSON�𑗐M���܂��B
��̓I�ɂ�`{"code": "R", "text": "�F�������e�L�X�g" }`�̂悤��JSON���������܂��B
�������nodecg�Ŏg�������������ł��B

������̕��@����x���s�����ꍇ�͈�x�F�����~�߂čăX�^�[�g����܂ł͏�������΂���܂��B

## ���̑�

���̂Ƃ���AIssue��Pull Request�ɑΉ�����\��͂���܂���B

���C�Z���X��MIT�ł��B
�𕶂�LICENCE������񂭂������B